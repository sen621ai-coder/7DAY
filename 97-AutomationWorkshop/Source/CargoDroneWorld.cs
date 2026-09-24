using System;
using System.Collections.Generic;
using System.Linq;

namespace YFAutomation.CargoDrones
{
    // The adapter resolves a persisted binding against today's native tile. It
    // preserves native ownership and never substitutes a replacement at its cell.
    public interface ICargoWorldAdapter
    {
        bool OwnerOnline(string owner);
        bool HubExists(CargoHubConfiguration hub);
        bool Powered(CargoHubConfiguration hub);
        CargoPoint Home(CargoHubConfiguration hub);
        bool Resolve(CargoBinding binding,out ICargoDurableEndpoint endpoint,out CargoPoint approach,out CargoHold hold);
        ICargoAirspace OpenAirspace(Guid flight);
        void ReleaseAirspace(Guid flight);
    }
    public interface ICargoRecoveryAdapter
    {
        bool ResolveRecovery(CargoHubConfiguration hub,Guid flight,out ICargoDurableEndpoint endpoint);
    }

    public sealed class CargoHubState
    {
        public readonly CargoHubConfiguration Configuration;
        public readonly Guid Flight;
        public readonly CargoBinding ShipmentSource,ShipmentTarget;
        public readonly CargoPosition? ShipmentEntrance;
        public readonly long Battery;
        public readonly int SourceCursor;
        public readonly bool Removed;
        public CargoHubState(CargoHubConfiguration configuration,Guid flight,CargoBinding source,CargoBinding target,long battery,int cursor,bool removed,CargoPosition? shipmentEntrance=null)
        {
            if(configuration==null||battery<0||battery>600000||cursor<0||cursor>=CargoRules.MaxSources)throw new ArgumentException("Invalid hub checkpoint");
            if(flight==Guid.Empty?(source!=null||target!=null):(source==null||target==null))throw new ArgumentException("Shipment bindings required exactly when a flight exists");
            if(flight==Guid.Empty&&shipmentEntrance.HasValue)throw new ArgumentException("Shipment entrance requires a flight");
            if(source!=null&&(source.WorldId!=configuration.WorldId||target.WorldId!=configuration.WorldId||source.EndpointId==target.EndpointId))throw new ArgumentException("Invalid shipment bindings");
            Configuration=configuration;Flight=flight;ShipmentSource=source;ShipmentTarget=target;ShipmentEntrance=shipmentEntrance;Battery=battery;SourceCursor=cursor;Removed=removed;
        }
    }

    public sealed class CargoWorldState
    {
        readonly CargoMissionState[] missions;
        readonly CargoHubState[] hubs;
        public CargoMissionState[] Missions{get{return (CargoMissionState[])missions.Clone();}}
        public CargoHubState[] Hubs{get{return (CargoHubState[])hubs.Clone();}}
        public CargoWorldState(IEnumerable<CargoMissionState> missions,IEnumerable<CargoHubState> hubs)
        {
            this.missions=missions.ToArray();this.hubs=hubs.ToArray();
            if(this.hubs.Length>16||this.hubs.Any(h=>h==null)||this.hubs.Select(h=>h.Configuration.HubId).Distinct().Count()!=this.hubs.Length||this.hubs.GroupBy(h=>h.Configuration.Owner).Any(g=>g.Count()>4))throw new ArgumentException("World hub limits or duplicate identity");
            if(this.hubs.Select(h=>h.Configuration.Position).Distinct().Count()!=this.hubs.Length)throw new ArgumentException("Duplicate hub position");
            var assigned=this.hubs.Where(h=>h.Flight!=Guid.Empty).Select(h=>h.Flight).ToArray();
            if(assigned.Distinct().Count()!=assigned.Length||assigned.Length!=this.missions.Length||this.missions.Any(m=>m==null||!assigned.Contains(m.Id)))throw new ArgumentException("Every mission must have exactly one hub");
            foreach(var hub in this.hubs.Where(h=>h.Flight!=Guid.Empty))
            {
                var m=this.missions.Single(s=>s.Id==hub.Flight);
                if(m.World!=hub.Configuration.WorldId||m.Owner!=hub.Configuration.Owner||m.Source!=hub.ShipmentSource.EndpointId||m.Target!=hub.ShipmentTarget.EndpointId||m.Battery!=hub.Battery||hub.Removed&&m.Phase!=CargoPhase.RecoveryOnly)throw new ArgumentException("Hub/mission checkpoint mismatch");
            }
        }
    }

    public sealed class CargoHubStatus
    {
        public readonly CargoHubConfiguration Configuration;
        public readonly Guid Flight;
        public readonly CargoPhase Phase;
        public readonly CargoBinding ShipmentTarget;
        public readonly CargoHold Hold;
        public readonly CargoPoint Position;
        public readonly long Battery;
        public readonly bool Powered;
        public readonly int Packages;
        public readonly string Message;
        internal CargoHubStatus(CargoHubConfiguration config,CargoMission mission,long battery,CargoPoint home,string message,CargoBinding shipmentTarget=null,bool powered=false)
        {Configuration=config;ShipmentTarget=shipmentTarget;Flight=mission==null?Guid.Empty:mission.Id;Phase=mission==null?CargoPhase.Docked:mission.Phase;Hold=mission==null?CargoHold.None:mission.Hold;Position=mission==null?home:mission.Position;Battery=mission==null?battery:mission.Battery;Packages=mission==null?0:CargoPlanner.Count(mission.Cargo);Message=message;Powered=powered;}
    }

    // One server-thread coordinator per world. Native inventory access stays in
    // the adapter; all admission, manifests and checkpoint ordering live here.
    public sealed class CargoWorldService
    {
        sealed class Hub
        {
            public CargoHubConfiguration Config;
            public CargoMission Mission;
            public CargoBinding Source,Target;
            public CargoPosition? ShipmentEntrance;
            public long Battery=600000;
            public int Cursor;
            public double NextEmptyPoll;
            public bool Removed;
            public bool Suspended;
            public CargoRecoveryDelivery Recovery;
            public string Message="等待配置";
        }
        const double EmptyPollSeconds=5;
        readonly Guid world;
        readonly CargoFileJournal journal;
        readonly CargoCheckpointStore checkpoints;
        readonly ICargoWorldAdapter adapter;
        readonly CargoRules rules=new CargoRules();
        readonly List<Hub> hubs=new List<Hub>();
        readonly CargoSourceReservation reservations=new CargoSourceReservation();
        int cursor;
        double activeTime;
        long sinceCheckpoint;
        bool faulted;
        bool dirty;
        public bool Faulted{get{return faulted;}}
        public string Failure{get;private set;}
        public int ActiveFlights{get{return hubs.Count(h=>h.Mission!=null&&h.Mission.Phase!=CargoPhase.Docked&&h.Mission.Phase!=CargoPhase.RecoveryOnly);}}
        bool Ready{get{return hubs.All(h=>(h.Mission==null||h.Mission.CheckpointReady)&&(h.Recovery==null||h.Recovery.State!=CargoRecoveryDeliveryState.Saving));}}
        public CargoWorldService(Guid world,CargoFileJournal journal,CargoCheckpointStore checkpoints,ICargoWorldAdapter adapter)
        {
            if(world==Guid.Empty||journal==null||journal.WorldId!=world||checkpoints==null||adapter==null)throw new ArgumentException("Invalid world service");
            this.world=world;this.journal=journal;this.checkpoints=checkpoints;this.adapter=adapter;
        }
        void Healthy(){if(faulted)throw new InvalidOperationException("World cargo service requires recovery: "+Failure);}
        Hub Find(Guid id){var h=hubs.SingleOrDefault(v=>v.Config.HubId==id);if(h==null)throw new ArgumentException("Unknown hub");return h;}
        public CargoHubStatus[] Status()
        {return hubs.Select(h=>new CargoHubStatus(h.Config,h.Mission,h.Battery,adapter.Home(h.Config),h.Message,h.Target,adapter.Powered(h.Config))).ToArray();}
        public void Register(CargoHubConfiguration config)
        {
            Healthy();if(!Ready)throw new InvalidOperationException("Inventory checkpoint pending");
            if(config==null||config.WorldId!=world||hubs.Count>=16||hubs.Count(h=>h.Config.Owner==config.Owner)>=4||hubs.Any(h=>h.Config.HubId==config.HubId||h.Config.Position.Equals(config.Position)))throw new InvalidOperationException("Hub identity or capacity unavailable");
            if(!adapter.HubExists(config))throw new InvalidOperationException("Hub incarnation missing");
            hubs.Add(new Hub{Config=config});Publish();
        }
        static CargoPoint? EntrancePoint(CargoPosition? entrance)
        {return entrance.HasValue?(CargoPoint?)new CargoPoint(entrance.Value.X+.5,entrance.Value.Y+2.2,entrance.Value.Z+.5):null;}
        public void Configure(Guid id,string actor,long revision,CargoHubConfiguration replacement,bool applyEntranceToShipment=false)
        {
            Healthy();var h=Find(id);
            if(actor!=h.Config.Owner)throw new UnauthorizedAccessException("Hub owner required");
            if(!Ready||h.Removed||h.Config.Revision!=revision||replacement==null||replacement.WorldId!=world||replacement.HubId!=id||replacement.Owner!=actor||!replacement.Position.Equals(h.Config.Position)||replacement.Revision<revision||replacement.Revision>checked(revision+1))throw new InvalidOperationException("Refresh hub configuration");
            if(applyEntranceToShipment&&h.Mission!=null)
            {
                h.Mission.SetDeliveryEntrance(EntrancePoint(replacement.Entrance));h.ShipmentEntrance=replacement.Entrance;
            }
            h.Config=replacement;
            h.NextEmptyPoll=0;Publish();
        }
        public void Recall(Guid id,string actor)
        {Healthy();var h=Find(id);if(actor!=h.Config.Owner)throw new UnauthorizedAccessException();h.Mission?.Recall();if(Ready)Publish();}
        public void Restore(CargoWorldState state)
        {
            Healthy();if(hubs.Count!=0||state.Hubs.Any(h=>h.Configuration.WorldId!=world))throw new InvalidOperationException("Restore requires an empty matching world");
            try
            {
                foreach(var saved in state.Hubs)
                {
                    var h=new Hub{Config=saved.Configuration,Battery=saved.Battery,Cursor=saved.SourceCursor,Removed=saved.Removed,Source=saved.ShipmentSource,Target=saved.ShipmentTarget,ShipmentEntrance=saved.ShipmentEntrance};
                    if(saved.Flight!=Guid.Empty)h.Mission=CargoMission.Restore(state.Missions.Single(m=>m.Id==saved.Flight),adapter.OpenAirspace(saved.Flight),journal,EntrancePoint(saved.ShipmentEntrance));
                    hubs.Add(h);
                }
                if(ActiveFlights>4)throw new InvalidOperationException("Saved active-flight limit exceeded");
            }
            catch(Exception ex){Fail(ex);throw;}
        }
        CargoWorldState Capture(bool dock)
        {
            var missions=hubs.Where(h=>h.Mission!=null).Select(h=>dock&&h.Mission.Phase==CargoPhase.Docking?h.Mission.CaptureDocked():h.Mission.Capture()).ToArray();
            return new CargoWorldState(missions,hubs.Select(h=>new CargoHubState(h.Config,h.Mission==null?Guid.Empty:h.Mission.Id,h.Source,h.Target,h.Mission==null?h.Battery:h.Mission.Battery,h.Cursor,h.Removed,h.Mission==null?null:h.ShipmentEntrance)));
        }
        public void Checkpoint(){Healthy();if(!Ready)throw new InvalidOperationException("Inventory checkpoint pending");Publish();}
        void Publish()
        {
            try
            {
                checkpoints.SaveWorld(Capture(true),journal);sinceCheckpoint=0;dirty=false;
                foreach(var h in hubs.Where(v=>v.Mission!=null&&v.Mission.Phase==CargoPhase.Docking))
                {h.Mission.ConfirmDocked();reservations.ReleaseFlight(h.Mission.Id);adapter.ReleaseAirspace(h.Mission.Id);h.Message="已停靠";}
                foreach(var h in hubs.Where(v=>v.Removed&&v.Mission!=null))
                {reservations.ReleaseFlight(h.Mission.Id);adapter.ReleaseAirspace(h.Mission.Id);}
                foreach(var h in hubs.Where(v=>v.Mission!=null&&!v.Suspended&&!adapter.OwnerOnline(v.Config.Owner)))
                {
                    // The checkpoint includes the last committed cargo and the
                    // exact route before any observer can be released.
                    adapter.ReleaseAirspace(h.Mission.Id);reservations.ReleaseFlight(h.Mission.Id);h.Suspended=true;
                }
            }
            catch(Exception ex){Fail(ex);throw;}
        }
        void Fail(Exception ex){faulted=true;Failure=ex.Message;foreach(var h in hubs)h.Message="持久化或库存异常，等待恢复";}
        static void RequireHealthyMission(CargoMission mission)
        {if(!mission.CheckpointReady&&mission.Hold==CargoHold.RecoveryRequired)throw new InvalidOperationException("Cargo transfer outcome requires recovery");}
        public void Tick(long elapsed,bool worldPaused=false)
        {
            if(elapsed<0)throw new ArgumentOutOfRangeException("elapsed");Healthy();long step=Math.Min(100,elapsed);
            try
            {
                bool changed=false;
                // Empty tombstones can release their position/capacity only after
                // their removal or complete recovery was checkpointed.
                if(Ready&&hubs.RemoveAll(h=>h.Removed&&(h.Mission==null||CargoPlanner.Count(h.Mission.Cargo)==0))>0){Publish();changed=true;}
                if(!worldPaused){activeTime+=step/1000.0;sinceCheckpoint=Math.Min(5000,sinceCheckpoint+step);}
                foreach(var h in hubs)
                {
                    if(h.Recovery!=null&&h.Recovery.State==CargoRecoveryDeliveryState.Saving)
                    {
                        h.Recovery.Poll();
                        if(h.Recovery.State==CargoRecoveryDeliveryState.RecoveryRequired)throw new InvalidOperationException("Recovery crate transfer requires reconciliation");
                        changed|=h.Recovery.State==CargoRecoveryDeliveryState.Completed;
                    }
                    var m=h.Mission;if(m==null)continue;
                    long revision=m.CargoRevision,battery=m.Battery;var phase=m.Phase;var previousHold=m.Hold;
                    bool online=adapter.OwnerOnline(h.Config.Owner);
                    if(h.Suspended&&online&&!worldPaused&&!h.Removed)
                    {
                        // Reconstruct navigation with a new adapter; the old
                        // native airspace has been released, not merely paused.
                        m=CargoMission.Restore(m.Capture(),adapter.OpenAirspace(m.Id),journal);h.Mission=m;h.Suspended=false;
                    }
                    CargoHold hold=CargoHold.None;ICargoDurableEndpoint endpoint;CargoPoint approach;
                    if(online&&!worldPaused&&(phase==CargoPhase.Loading||phase==CargoPhase.Unloading)&&m.CheckpointReady)
                    {
                        var binding=phase==CargoPhase.Loading?h.Source:h.Target;
                        if(!adapter.Resolve(binding,out endpoint,out approach,out hold)&&hold==CargoHold.None)m.Recall();
                    }
                    m.Tick(step,online,worldPaused,hold);
                    RequireHealthyMission(m);
                    if(m.Phase==CargoPhase.Docked)m.ChargeDocked(step,adapter.Powered(h.Config),online,worldPaused);
                    if(m.Phase!=CargoPhase.ToSource&&m.Phase!=CargoPhase.Loading)reservations.ReleaseFlight(m.Id);
                    changed|=revision!=m.CargoRevision||phase!=m.Phase;
                    dirty|=battery!=m.Battery||previousHold!=m.Hold;
                    changed|=!online&&!h.Suspended;
                    if(!adapter.HubExists(h.Config)&&!h.Removed&&m.CheckpointReady)
                    {if(!m.FreezeForRecovery())throw new InvalidOperationException("Cannot freeze removed hub shipment");h.Removed=true;changed=true;h.Message="停机坪已移除，等待回收";}
                    else if(!online)h.Message="所有者离线，航班冻结";
                    else if(m.Hold!=CargoHold.None)h.Message=m.Hold.ToString();
                    else h.Message=m.Phase.ToString();
                }
                // Settle every prepared operation before publishing or admitting
                // another transfer. Docking releases leases only after this save.
                if(!Ready)return;
                if(changed||dirty&&sinceCheckpoint>=5000)Publish();
                if(worldPaused)return;
                var order=hubs.Skip(cursor).Concat(hubs.Take(cursor)).ToArray();
                if(hubs.Count!=0)cursor=(cursor+1)%hubs.Count;
                foreach(var h in order)
                {
                    if(h.Removed)
                    {
                        var recovery=adapter as ICargoRecoveryAdapter;ICargoDurableEndpoint crate;
                        if(h.Mission!=null&&CargoPlanner.Count(h.Mission.Cargo)>0&&recovery!=null&&recovery.ResolveRecovery(h.Config,h.Mission.Id,out crate))
                        {
                            if(h.Recovery==null)h.Recovery=new CargoRecoveryDelivery(h.Mission,journal,h.Mission.Id,h.Config.HubId);
                            if(h.Recovery.State==CargoRecoveryDeliveryState.WaitingForContainer)h.Recovery.Begin(crate);
                            if(h.Recovery.State==CargoRecoveryDeliveryState.RecoveryRequired)throw new InvalidOperationException("Recovery crate transfer requires reconciliation");
                            if(h.Recovery.State==CargoRecoveryDeliveryState.Completed){Publish();return;}
                            if(h.Recovery.State==CargoRecoveryDeliveryState.Saving)return;
                        }
                        continue;
                    }
                    if(!adapter.OwnerOnline(h.Config.Owner))continue;
                    if(!adapter.HubExists(h.Config))
                    {h.Removed=true;h.Message="停机坪已移除";Publish();continue;}
                    var m=h.Mission;
                    if(m!=null&&m.TransferReady)
                    {
                        ICargoDurableEndpoint endpoint;CargoPoint at;CargoHold hold;
                        if(adapter.Resolve(m.Phase==CargoPhase.Loading?h.Source:h.Target,out endpoint,out at,out hold))
                        {m.BeginTransfer(journal,endpoint);RequireHealthyMission(m);if(Ready)Publish();return;}
                        continue;
                    }
                    if(m!=null&&m.Phase!=CargoPhase.Docked)continue;
                    if(m==null&&adapter.Powered(h.Config)){long before=h.Battery;h.Battery=Math.Min(600000,h.Battery+step*10);dirty|=before!=h.Battery;}
                    if(h.Config.Paused){h.Message="已暂停，通电时充电";continue;}
                    if(!adapter.Powered(h.Config)){h.Message="等待供电：给4格内的自动化供电接口接线";continue;}
                    if(ActiveFlights>=4){h.Message="等待飞行名额";continue;}
                    if(activeTime<h.NextEmptyPoll)continue;
                    bool allSourcesEmpty;
                    if(TryDepart(h,out allSourcesEmpty)){h.NextEmptyPoll=0;Publish();return;}
                    h.NextEmptyPoll=allSourcesEmpty?activeTime+EmptyPollSeconds:0;
                }
            }
            catch(CargoEndpointUnavailableException ex)
            {
                // Only a pre-operation availability race is retryable. Once a
                // mission has failed an in-progress transfer it requires recovery.
                if(hubs.Any(h=>h.Mission!=null&&!h.Mission.CheckpointReady&&h.Mission.Hold==CargoHold.RecoveryRequired)){Fail(ex);throw;}
            }
            catch(Exception ex){Fail(ex);throw;}
        }
        bool TryDepart(Hub h,out bool allSourcesEmpty)
        {
            allSourcesEmpty=false;
            ICargoDurableEndpoint endpoint;CargoPoint targetPoint;CargoHold hold;
            bool carrying=h.Mission!=null&&CargoPlanner.Count(h.Mission.Cargo)>0;
            var target=carrying?h.Target:h.Config.Target;
            if(target==null||!adapter.Resolve(target,out endpoint,out targetPoint,out hold)){h.Message="目标未配置或暂不可用";return false;}
            var targetInventory=endpoint.Snapshot();if(targetInventory.Busy)return false;
            long battery=h.Mission==null?h.Battery:h.Mission.Battery;
            var home=adapter.Home(h.Config);
            if(h.Mission!=null&&h.Mission.ReturnReason==CargoMissionReturnReason.EnergyLow&&battery<600000)
            {h.Message="低电量返航：充满后重试";return false;}
            if(carrying)
            {
                if(CargoPlanner.Unload(targetInventory,h.Mission.Cargo,h.Config.Owner).Moved==0)return false;
                if(!DepartureEnergy(h,battery,CargoMission.EstimateSortieSeconds(home,null,targetPoint,EntrancePoint(h.ShipmentEntrance))))return false;
                h.Mission=h.Mission.RetryDelivery(adapter.OpenAirspace(h.Mission.Id));h.Message="优先重送旧货";return true;
            }
            var sources=h.Config.Sources;
            bool sourceUnavailable=false,sourceHasCargo=false,energyDenied=false;
            for(int i=0;i<sources.Length;i++)
            {
                int index=(h.Cursor+i)%sources.Length;var source=sources[index];CargoPoint pickup;
                if(!adapter.Resolve(source,out endpoint,out pickup,out hold)){sourceUnavailable=true;continue;}
                var plan=CargoPlanner.Load(endpoint.Snapshot(),new CargoItem[6],h.Config.Owner,6);
                if(plan.Moved==0)continue;
                sourceHasCargo=true;
                if(CargoPlanner.Unload(targetInventory,plan.CargoAfter,h.Config.Owner).Moved==0)continue;
                // Include the recorded-corridor return, handling and approach
                // overhead. In-flight energy checks also budget actual detours.
                if(!DepartureEnergy(h,battery,CargoMission.EstimateSortieSeconds(home,pickup,targetPoint,EntrancePoint(h.Config.Entrance)))){energyDenied=true;continue;}
                Guid flight=Guid.NewGuid();if(!reservations.TryReserve(source.EndpointId,flight,activeTime,home.Distance(pickup)/2))continue;
                if(h.Mission!=null)adapter.ReleaseAirspace(h.Mission.Id);
                h.ShipmentEntrance=h.Config.Entrance;
                h.Mission=new CargoMission(world,flight,source.EndpointId,target.EndpointId,h.Config.Owner,adapter.OpenAirspace(flight),home,pickup,targetPoint,battery,entrance:EntrancePoint(h.ShipmentEntrance));
                h.Source=source;h.Target=target;h.Cursor=(index+1)%sources.Length;h.Message="飞往采集设备";return true;
            }
            allSourcesEmpty=!sourceHasCargo&&!sourceUnavailable;
            if(!energyDenied)h.Message=allSourcesEmpty?"等待矿机产出（每5秒检查）":"等待物资、容量或充电";return false;
        }
        bool DepartureEnergy(Hub h,long battery,double seconds)
        {
            if(rules.CanDepart(battery,seconds))return true;
            double required=seconds*1000+180000;
            h.Message=required>600000?"航程预算超出满电能力，请缩短航线":"等待充电：航程预算需至少 "+Math.Ceiling(required/6000)+"%（含入口绕行和返航余量）";
            return false;
        }
    }
}
