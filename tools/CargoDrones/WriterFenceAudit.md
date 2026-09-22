# 原生端点写入栅栏审计

状态：正常世界已接入原生库存适配器、访问会话和写入栅栏；真实双客户端时序、完整崩溃矩阵与所有第三方写者尚未验收。构建未安装到正式游戏。当前证据见 [运行时修复与回归](RuntimeFixes-2026-09-22.md)。

对应Assembly-CSharp MVID：`229796d0-95ca-4662-b426-1a6f1f1596ed`。

## 2026-09-22 最新接线

访问会话与库存写入钩子现在随模组初始化安装，早于世界内玩家访问。正常服务端可创建端点，但交接必须满足登记有效、原生锁释放、尾序列排空和实例匹配。角色在场不再直接禁用适配器。新增方块受损入口保护，避免尚未提交的库存被破坏掉落；启动恢复对待定端点先隔离，再通过持久保存确认恢复结果。原生实体夹具已覆盖授权与拒绝路径，仍未证明真实客户端关闭、重连和网络时序。

以下保留先前审计过程，文中“仅 QA 安装”等是历史状态。

## 2026-09-22 较早补充

- 新增候选原生访问会话桥接，仅QA安装且要求在玩家访问前启动。支持的Collector/玩家Storage及其特征访问在首次开锁时就开始跟踪；绑定时必须等已有会话真正排空，不能从“当前没开箱”推断没有尾包。临时访问身份与持久端点身份分开，旧Collector仍不因开箱或绑定获得主人。
- 新增消息：能力声明、服务端会话令牌、带序号的原生库存包、携带最终序号的关闭消息。服务端以实际ClientInfo对象和令牌验证发送者，按连续序号处理有界缓存；原生锁仍在、序号缺口、实例替换或会话故障均不能交接。原生包读取采用单次具体实例许可，回调不继承许可。
- 100组乱序/重复尾包核心测试通过；四种消息的原生编解码和注册通过。尚未证明真实客户端所有关闭回调、重连和底层传输时序，不能去掉QA限制。该桥接没有在正式游戏中安装或强制所有玩家使用。
- 新增持久日志追加前的完整历史检查：旧航程不能使用已消费的CargoRevision再次PREPARE；这类拒绝发生在原生Apply之前。当前实现的长历史性能尚未验收。

- P3后/P4前已完成一次真实外部强制终止及新进程恢复，12项原生检查通过。测试加载来源前暂停Collector生产，重读真实原生库存和WAL，再确认保存并补一次COMMIT；不是所有端点的正式启动恢复器，也不是完整P0～P5验收。
- 复验发现原生`Chunk.IsLocked`会随复制、装饰、光照、再生成、卸载、保存、网络、水模拟标志变化，不能把同一主线程调用链里先前一次就绪检查视为后续始终就绪。恢复状态机现跨帧保留步骤，仅重试明确在操作前抛出的暂不可用异常。
- 已在Production入口与世界完成回调之前检查机器/来源/目标；外部灌溉水箱通过Work.Ready参与完成前检查；WaterSystem.Pump增加机器/水箱检查；Conveyors在整个批次第一次Array.Copy之前统一检查参与者仍存在且不忙。不能只跳过忙碌的某一个输出端而保留其他端扣货。
- 上述版本43项原生库存回归通过，覆盖生产/抽水入口拒绝和区块锁定时延后快照；并不代表传送带整网、外部灌溉回调重入或双客户端已经验收。
- 原生IL证实`NetPackageTileEntity.ProcessPackage`按坐标及方块类型找到端点后直接执行`tile.read`，包内没有库存版本；`LockManager.UnlockRequestServer`移除玩家锁并调用解锁回调，未发现它与此前库存快照包之间的版本/序列确认。`NetPackage.Sender`可以识别连接，但身份校验本身不能证明尾包已排空。没有实现盲目丢弃、延迟两秒或无条件重放旧完整库存快照的方案。
- 进一步确认：`NetPackageTileEntity`继承默认Channel=0及ShouldProcess=true；`SendToServer`按Channel选连接后调用AddToSendQueue；`ConnectionManager.ProcessPackages`按取得的列表顺序调用ProcessPackage。候选关闭确认消息必须与库存更新进入同一有序通道，并在客户端停止生成该访问会话的更新之后发送。上述三处证据仍不足以证明各底层连接的压缩/分包队列顺序，以及所有客户端关箱回调都已完成；不能把一个普通ACK当作已完成的库存交接协议。证据报告保留在`.local-tests/CargoDrones/network-delivery-il.txt`及`connections-api.txt`。

## 已定位入口

| 路径 | 实际接口或代码 | 后续约束 |
| --- | --- | --- |
| Collector生产 | `TileEntityCollector.UpdateTick`、`HandleUpdate`、`handleUpdateForOutputType` | PREPARE到COMMIT期间暂停写入；不重置生产计时和配方状态 |
| Collector库存API | `Items` setter、`UpdateSlot`、`TryStackItem`、`AddItem`、`RemoveItem` | 持有事务授权时允许执行；其他写者等待或获得正确的未执行结果 |
| 玩家箱库存API | `TEFeatureStorage.items` setter、`UpdateSlot`、`RemoveItems`、`TryStackItem`、`AddItem`、`Read` | 不能跳过写入后还向调用者报告成功，否则调用者可能先扣除另一端库存 |
| 玩家开箱 | `LockManager.LockRequestServer`最终调用`ILockTarget.CanLockOnServer` | Collector继承`TileEntity.CanLockOnServer`；复合箱需核对实际特征目标；不能只改`IsLockedServer`查询 |
| 玩家库存网络更新 | `NetPackageTileEntity.ProcessPackage`按坐标取得端点后调用`TileEntity.read` | 需要处理已经关闭窗口但仍在途的合法更新，避免直接丢包造成物资丢失；需实际双客户端时序测试 |
| 当前自动化直接数组写入 | `Logistics.Busy`、传送带／生产／WaterSystem／TurretFeed | `items`返回原始数组，单独修补setter不足。需逐一检查提交前的忙碌判断和延迟完成回调 |
| 世界保存 | `RegionFileChunkSnapshot.Update` → `Chunk.save` → `Chunk.write` | 与库存和提交标记更新共享序列化锁；保存快照不得采集到半次事务 |
| 世界保存队列 | `RegionFileManager.SaveChunkSnapshot`在队列锁内同步采集或重写快照，再启动后台保存 | 后续保存可能复用／更新同一快照对象；候选回执绑定实际字节，改变后不能确认旧事务 |
| 端点移除、方块破坏 | Collector和复合箱销毁／掉落路径 | 不能用禁止手动拾取代替战斗破坏处理；须与G04唯一回收协议衔接 |

## 当前已实现的边界

- `CargoNativeValidationEndpoint`仅在显式QA启动参数、测试GameName、无玩家、当前服务器世界、匹配的主线程和原生实例身份下工作。完整原生ItemStack数组与事务版本标记在ChunkTransferLock内更新，随后交给已验证的异步队列回执。未确定结果时保留端点栅栏，不自动撤销或释放已应用的库存。
- 端点栅栏按实际TileEntity实例共享，另一个适配器不能重复获取。隔离安装时拦截Collector生产入口及TileEntity开锁入口；Logistics.Busy与货运只读快照识别栅栏。此覆盖不包括所有原生库存API、复合箱特征开锁入口、网络尾包、直接数组写者或破坏掉落，不能宣称已完成全写者隔离。
- 已通过第一轮真实Collector取货试验（14项检查）：原生数组扣除一包、完整值和标记同快照保存、竞争适配器拒绝、原生回执后COMMIT、重新打开WAL恢复已提交货物、释放区块回到基线。此轮无玩家、无连接到测试容器的其他设备，未证明并发玩家使用安全。
- 第二轮21项原生检查全部通过：增加真实`cntWoodWritableCrate`玩家储物箱，使用同一FlightId及连续CargoRevision执行卸货，确认完整物品数据、库存/版本同快照、两端总量守恒、卸货提交后货舱日志重建为空。只能证明受控隔离场景中的真实读写链路，不能据此去掉QA限制。
- 新增`CargoNativeWriteGuards`隔离安装：Collector/Storage的AddItem、TryStackItem拒绝时不修改传入ItemStack；Storage.RemoveItems返回0；UpdateSlot、数组setter、RemoveItem、SetEmpty、SetContainerSize、SlotLocks替换在冻结时明确抛出拒绝，不伪报成功。库存事务只获得一次、绑定具体数组实例的setter许可，进入原生setter前即消耗许可，回调不继承授权。TEFeatureAbs.CanLockOnServer补齐复合储物箱特征开锁入口。
- 第一轮守卫测试发现：TEFeatureStorage.Write在SlotLocks为空时会写入全false格锁数组，触发守卫导致原生保存失败。通过原生IL确认后，改为获取栅栏前在区块序列化锁内完成等价格锁初始化。修复后原生34项检查全部通过，包括实际库存API拒绝、调用方输入不变、生产Tick冻结、特征开锁拒绝、原生保存及双端数量守恒。
- 这些API守卫不覆盖此前已拿到的原始数组引用，也不提供“调用方先扣货再写目标失败”的补偿事务；这类调用方仍必须在源端扣货之前检查可用性。网络Read及迟到更新包没有被直接丢弃或吞掉，仍是正式启用的阻塞项。
- `CargoPreparedRecovery`从完整WAL验证未决PREPARE：原库存前像可确认为ABORT；库存后像取得新原生耐久回执后才COMMIT，绝不重复Apply；未知状态持有栅栏等待恢复依据。它还不是世界启动扫描、导航检查点恢复或完整P0～P5崩溃协议。

- `CargoNativeQueuedSaves`在原生快照更新外围持有现有`ChunkTransferLock`，并解码真正生成的快照，验证端点身份、事务标记和完整库存。
- `CargoNativeSaveReceipts`核对同一快照的字节摘要和写入位置，在原生区域数据及头部写入后显式Flush(true)，使用新区域读取器重新读取磁盘头部及数据。
- 原生回执只证明指定快照已完成这一写入路径；它不证明所有库存写者被约束，也不证明后续错误保存不会覆盖该快照。
- `CargoJournalReplay`仅重放完整WAL历史，不会用缺失历史的PREPARE前像推造货物；原生加载恢复栅栏和检查点尚未接入。

## 必须继续验证

1. 获取栅栏之前，玩家原生访问锁、关闭窗口尾包和服务器状态已经一致；冻结期间开箱应得到原生可理解的拒绝反馈。
2. 双端扣除／写入类调用不能因被拦截而只完成其中一端。
3. P0～P5强制中断、原生自动保存并发、重复保存和重启的完整物品值数量守恒。
4. 已提交端点后续被玩家消费、生产续填或销毁时，恢复依据为有效版本／检查点链，不能只比较当前数量。

已完成的独立快照读回或正常进程重启，均不能替代以上验证。
