using System;
using System.Collections.Generic;
using System.Linq;

namespace YFAutomation.CargoDrones
{
    public sealed class CargoBinding
    {
        public readonly Guid WorldId,EndpointId,Incarnation;
        public readonly CargoPosition Position;
        public readonly string Owner,BlockName;
        public CargoBinding(Guid world,Guid endpoint,Guid incarnation,CargoPosition position,string owner,string block)
        {
            if(world==Guid.Empty||endpoint==Guid.Empty||incarnation==Guid.Empty||string.IsNullOrEmpty(owner)||string.IsNullOrEmpty(block))
                throw new ArgumentException("Binding requires a server-confirmed persistent identity");
            WorldId=world;EndpointId=endpoint;Incarnation=incarnation;Position=position;Owner=owner;BlockName=block;
        }
        public bool Matches(CargoBinding current)
        {
            return current!=null&&WorldId==current.WorldId&&EndpointId==current.EndpointId&&Incarnation==current.Incarnation&&
                Position.Equals(current.Position)&&Owner==current.Owner&&BlockName==current.BlockName;
        }
    }
    // This model accepts only server-resolved identities. A network/UI request must
    // supply positions and revisions, never caller-authored owner or endpoint IDs.
    public sealed class CargoHubConfiguration
    {
        readonly CargoBinding[] sources;
        public readonly Guid WorldId,HubId;
        public readonly CargoPosition Position;
        public readonly string Owner;
        public readonly long Revision;
        public readonly CargoBinding Target;
        public readonly bool Paused;
        public CargoBinding[] Sources{get{return (CargoBinding[])sources.Clone();}}
        public CargoHubConfiguration(Guid world,Guid hub,CargoPosition position,string owner)
            :this(world,hub,position,owner,0,new CargoBinding[0],null,false){}
        CargoHubConfiguration(Guid world,Guid hub,CargoPosition position,string owner,long revision,CargoBinding[] bindings,CargoBinding target,bool paused)
        {
            if(world==Guid.Empty||hub==Guid.Empty||string.IsNullOrEmpty(owner))throw new ArgumentException("Invalid hub identity");
            WorldId=world;HubId=hub;Position=position;Owner=owner;Revision=revision;sources=(CargoBinding[])bindings.Clone();Target=target;Paused=paused;
        }
        void CanEdit(string actor,long expectedRevision)
        {
            if(actor!=Owner)throw new UnauthorizedAccessException("Hub owner required");
            if(expectedRevision!=Revision)throw new InvalidOperationException("Configuration changed; refresh before editing");
        }
        void Validate(CargoBinding binding,CargoRules rules,bool source)
        {
            if(binding==null||rules==null)throw new ArgumentNullException("binding/rules");
            if(binding.WorldId!=WorldId)throw new InvalidOperationException("Endpoint must share hub world");
            if(binding.EndpointId==HubId)throw new InvalidOperationException("Hub cannot bind to itself");
            if(source?(!CargoRules.IsSource(binding.BlockName)||!rules.CanCollect(Position,binding.Position)):!rules.CanDeliver(Position,binding.Position))
                throw new InvalidOperationException("Endpoint type or range invalid");
        }
        CargoHubConfiguration Copy(CargoBinding[] bindings,CargoBinding target,bool paused)
        {return new CargoHubConfiguration(WorldId,HubId,Position,Owner,checked(Revision+1),bindings,target,paused);}
        public CargoHubConfiguration AddSource(string actor,long expectedRevision,CargoBinding source,CargoRules rules)
        {
            CanEdit(actor,expectedRevision);Validate(source,rules,true);
            var existing=sources.FirstOrDefault(s=>s.EndpointId==source.EndpointId);
            if(existing!=null){if(existing.Matches(source))return this;throw new InvalidOperationException("Source incarnation changed");}
            if(sources.Length>=CargoRules.MaxSources)throw new InvalidOperationException("Source limit reached");
            if(sources.Any(s=>s.Position.Equals(source.Position)))throw new InvalidOperationException("Old binding at this position must be removed first");
            return Copy(sources.Concat(new[]{source}).ToArray(),Target,Paused);
        }
        public CargoHubConfiguration RemoveSource(string actor,long expectedRevision,Guid source)
        {
            CanEdit(actor,expectedRevision);var updated=sources.Where(s=>s.EndpointId!=source).ToArray();
            return updated.Length==sources.Length?this:Copy(updated,Target,Paused);
        }
        // Native adapter must additionally prove target storage capability and access.
        // Changing this value never rewrites the manifest of cargo already in flight.
        public CargoHubConfiguration SetTarget(string actor,long expectedRevision,CargoBinding target,CargoRules rules)
        {
            CanEdit(actor,expectedRevision);if(target!=null)Validate(target,rules,false);
            return Target==null&&target==null||Target!=null&&Target.Matches(target)?this:Copy(sources,target,Paused);
        }
        public CargoHubConfiguration SetPaused(string actor,long expectedRevision,bool paused)
        {CanEdit(actor,expectedRevision);return paused==Paused?this:Copy(sources,Target,paused);}
        internal static CargoHubConfiguration Restore(Guid world,Guid hub,CargoPosition position,string owner,long revision,CargoBinding[] sources,CargoBinding target,bool paused)
        {
            if(revision<0||sources==null||sources.Length>CargoRules.MaxSources)throw new ArgumentException("Invalid saved configuration");
            var result=new CargoHubConfiguration(world,hub,position,owner);var rules=new CargoRules();
            foreach(var source in sources)result=result.AddSource(owner,result.Revision,source,rules);
            if(result.Sources.Length!=sources.Length)throw new ArgumentException("Duplicate saved source");
            result=result.SetTarget(owner,result.Revision,target,rules);
            return new CargoHubConfiguration(world,hub,position,owner,revision,sources,target,paused);
        }
    }
}
