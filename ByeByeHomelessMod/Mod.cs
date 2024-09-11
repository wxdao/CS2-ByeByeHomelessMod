using System.Runtime.CompilerServices;
using Colossal.Logging;
using Game;
using Game.Agents;
using Game.Buildings;
using Game.Citizens;
using Game.City;
using Game.Common;
using Game.Economy;
using Game.Modding;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Simulation;
using Game.Tools;
using Game.Triggers;
using Game.Vehicles;
using Unity.Burst.Intrinsics;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine.Scripting;
using Unity.Mathematics;
using Colossal.Json;
using Colossal.IO.AssetDatabase;
using Colossal;
using Game.Settings;
using Game.UI;
using System.Collections.Generic;
using static ByeByeHomelessMod.Setting;
using Game.Companies;
using Game.PSI;
using Colossal.PSI.Common;
using Colossal.Entities;
using Colossal.Serialization.Entities;

namespace ByeByeHomelessMod
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger($"{nameof(ByeByeHomelessMod)}.{nameof(Mod)}").SetShowsErrorsInUI(false);

        public static Mod Instance { get; private set; }

        public Setting Setting { get; private set; }

        public void OnLoad(UpdateSystem updateSystem)
        {
            Instance = this;

            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                log.Info($"Current mod asset at {asset.path}");

            Setting = new Setting(this);

            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Setting));

            log.Info($"Game version: {Game.Version.current.shortVersion}");
            if (Game.Version.current.shortVersion != "1.1.8f1")
            {
                NotificationSystem.Push("BBH_VERSION_NOTICE", "Bye Bye Homeless", "The mod is disabled due to unsupported game version.", null, null, null, ProgressState.Warning, null, delegate
                {
                    var dialog = new MessageDialog("Bye Bye Homeless", "The mod has been automatically shut down to avoid compatibility issues. Stay tuned for further updates.", "OK");
                    GameManager.instance.userInterface.appBindings.ShowMessageDialog(dialog, delegate (int msg)
                    {
                        NotificationSystem.Pop("BBH_VERSION_NOTICE");
                    });
                });
                log.Info($"Mod disabled.");
                return;
            }

            Setting.RegisterInOptionsUI();

            AssetDatabase.global.LoadSettings(nameof(Mod), Setting, new Setting(this));

            if (Setting.ShowUpdateNotice20240909)
            {
                NotificationSystem.Push("BBH_UPDATE_NOTICE", "Bye Bye Homeless", "The mod has been updated and re-enabled. Click to learn more.", null, null, null, ProgressState.None, null, delegate
                {
                    var dialog = new MessageDialog("Bye Bye Homeless", "There used to be a bug where citizens moving in were prioritized over the homeless when assigning houses, but that got fixed with the 1.1.8f game update. Now homeless citizens still searching for a place can move into new homes if you zone more for them.\n\n\n\nHowever, testing has shown that in some cases, homeless citizens who had already decided to move away can still get stuck forever. The mod has been updated to help them break free from limbo.", "OK");
                    GameManager.instance.userInterface.appBindings.ShowMessageDialog(dialog, delegate (int msg)
                    {
                        Setting.ShowUpdateNotice20240909 = false;
                        NotificationSystem.Pop("BBH_UPDATE_NOTICE");
                    });
                });
            }

            updateSystem.UpdateAt<ByeByeHomelessSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<ByeByeExtraCompanySystem>(SystemUpdatePhase.GameSimulation);
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
        }
    }

    [FileLocation("ModsSettings/ByeByeHomeless/setting.coc")]
    public class Setting : ModSetting
    {
        public Setting(IMod mod) : base(mod)
        {
            SetDefaults();
        }

        [SettingsUIHidden]
        public bool ShowUpdateNotice20240909 { get; set; }

        public bool EvictExtraCompany { get; set; }

        public override void SetDefaults()
        {
            ShowUpdateNotice20240909 = true;
            EvictExtraCompany = false;
        }
    }

    public class LocaleEN : IDictionarySource
    {
        private readonly Setting _mSetting;

        public LocaleEN(Setting setting)
        {
            _mSetting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { _mSetting.GetSettingsLocaleID(), "Bye Bye Homeless" },
                { _mSetting.GetOptionLabelLocaleID(nameof(Setting.EvictExtraCompany)), "Evict Ghost Companies" },
                { _mSetting.GetOptionDescLocaleID(nameof(Setting.EvictExtraCompany)), "[EXPERIMENTAL] When checked, companies without factories or offices will be evicted." },
            };
        }

        public void Unload()
        {
        }
    }

    public struct FindPropertyTimeout : IComponentData, IQueryTypeParameter, ISerializable
    {
        public uint m_SimulationFrame;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(m_SimulationFrame);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out m_SimulationFrame);
        }
    }


    public partial class ByeByeHomelessSystem : GameSystemBase
    {
        [BurstCompile]
        private struct ByeByeHomelessJob : IJobChunk
        {
            [ReadOnly]
            public EntityTypeHandle m_EntityType;

            [ReadOnly]
            public ComponentTypeHandle<Household> m_HouseholdType;

            [ReadOnly]
            public ComponentTypeHandle<MovingAway> m_MovingAwayType;

            [ReadOnly]
            public ComponentTypeHandle<FindPropertyTimeout> m_FindPropertyTimeoutType;

            [ReadOnly]
            public ComponentTypeHandle<PropertyRenter> m_PropertyRenterType;

            [ReadOnly]
            public BufferTypeHandle<HouseholdCitizen> m_HouseholdCitizenType;

            [ReadOnly]
            public ComponentLookup<TravelPurpose> m_TravelPurposeLookup;

            [ReadOnly]
            public ComponentLookup<CurrentTransport> m_CurrentTransportLookup;

            public EntityCommandBuffer.ParallelWriter m_CommandBuffer;

            public NativeQueue<StatisticsEvent>.ParallelWriter m_StatisticsQueue;

            public uint m_SimulationFrame;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<Entity> entityArray = chunk.GetNativeArray(m_EntityType);
                NativeArray<Household> householdArray = chunk.GetNativeArray(ref m_HouseholdType);
                BufferAccessor<HouseholdCitizen> householdCitizensBufferArray = chunk.GetBufferAccessor(ref m_HouseholdCitizenType);

                NativeArray<MovingAway> movingAwayArray = chunk.GetNativeArray(ref m_MovingAwayType);
                bool hasMovingAway = movingAwayArray.Length > 0;

                NativeArray<FindPropertyTimeout> findPropertyTimeoutArray = chunk.GetNativeArray(ref m_FindPropertyTimeoutType);
                bool hasFindPropertyTimeout = findPropertyTimeoutArray.Length > 0;

                NativeArray<PropertyRenter> propertyRenterArray = chunk.GetNativeArray(ref m_PropertyRenterType);
                bool hasPropertyRenter = propertyRenterArray.Length > 0;

                if (hasFindPropertyTimeout && (hasMovingAway || hasPropertyRenter))
                {
                    for (int i = 0; i < entityArray.Length; i++)
                    {
                        m_CommandBuffer.RemoveComponent<FindPropertyTimeout>(unfilteredChunkIndex, entityArray[i]);
                    }
                    return;
                }

                if (hasMovingAway)
                {
                    for (int i = 0; i < entityArray.Length; i++)
                    {
                        Entity entity = entityArray[i];
                        Household household = householdArray[i];

                        if (householdCitizensBufferArray.Length == 0)
                        {
                            m_CommandBuffer.AddComponent(unfilteredChunkIndex, entity, default(Deleted));
                            return;
                        }
                        DynamicBuffer<HouseholdCitizen> householdCitizens = householdCitizensBufferArray[i];
                        if (householdCitizens.Length == 0)
                        {
                            m_CommandBuffer.AddComponent(unfilteredChunkIndex, entity, default(Deleted));
                            return;
                        }

                        for (int j = 0; j < householdCitizens.Length; j++)
                        {
                            Entity citizen = householdCitizens[j].m_Citizen;

                            if (!m_CurrentTransportLookup.TryGetComponent(citizen, out var currentTransport))
                            {
                                m_StatisticsQueue.Enqueue(new StatisticsEvent
                                {
                                    m_Statistic = StatisticType.CitizensMovedAway,
                                    m_Change = 1
                                });
                                m_CommandBuffer.AddComponent(unfilteredChunkIndex, citizen, default(Deleted));
                                continue;
                            }

                            if (!m_TravelPurposeLookup.TryGetComponent(citizen, out var travelPurpose) || travelPurpose.m_Purpose != Game.Citizens.Purpose.MovingAway)
                            {
                                m_CommandBuffer.AddComponent(unfilteredChunkIndex, citizen, new TravelPurpose
                                {
                                    m_Purpose = Game.Citizens.Purpose.MovingAway,
                                });
                                m_CommandBuffer.AddComponent(unfilteredChunkIndex, currentTransport.m_CurrentTransport, new Target
                                {
                                    m_Target = Entity.Null,
                                });
                                continue;
                            }
                        }
                    }
                    return;
                }

                if (!hasPropertyRenter && !hasFindPropertyTimeout)
                {
                    for (int i = 0; i < entityArray.Length; i++)
                    {
                        m_CommandBuffer.AddComponent(unfilteredChunkIndex, entityArray[i], new FindPropertyTimeout
                        {
                            m_SimulationFrame = m_SimulationFrame
                        });
                    }
                    return;
                }

                if (!hasPropertyRenter && hasFindPropertyTimeout)
                {
                    for (int i = 0; i < entityArray.Length; i++)
                    {
                        FindPropertyTimeout findPropertyTimeout = findPropertyTimeoutArray[i];
                        if (m_SimulationFrame - findPropertyTimeout.m_SimulationFrame >= 10000)
                        {
                            m_CommandBuffer.AddComponent<MovingAway>(unfilteredChunkIndex, entityArray[i]);
                            m_CommandBuffer.RemoveComponent<PropertySeeker>(unfilteredChunkIndex, entityArray[i]);
                        }
                    }
                    return;

                }
            }

            void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
            }
        }


        private EntityQuery m_HomelessGroup;

        private EndFrameBarrier m_EndFrameBarrier;

        private CityStatisticsSystem m_CityStatisticsSystem;

        private SimulationSystem m_SimulationSystem;

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 262144 / 64;
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_EndFrameBarrier = base.World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_CityStatisticsSystem = base.World.GetOrCreateSystemManaged<CityStatisticsSystem>();
            m_SimulationSystem = base.World.GetOrCreateSystemManaged<SimulationSystem>();
            m_HomelessGroup = GetEntityQuery(ComponentType.ReadOnly<Household>(), ComponentType.Exclude<CommuterHousehold>(), ComponentType.Exclude<TouristHousehold>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            RequireForUpdate(m_HomelessGroup);
        }

        protected override void OnUpdate()
        {
            ByeByeHomelessJob byeByeHomelessJob = new ByeByeHomelessJob
            {
                m_EntityType = SystemAPI.GetEntityTypeHandle(),
                m_HouseholdType = SystemAPI.GetComponentTypeHandle<Household>(isReadOnly: true),
                m_MovingAwayType = SystemAPI.GetComponentTypeHandle<MovingAway>(isReadOnly: true),
                m_PropertyRenterType = SystemAPI.GetComponentTypeHandle<PropertyRenter>(isReadOnly: true),
                m_FindPropertyTimeoutType = SystemAPI.GetComponentTypeHandle<FindPropertyTimeout>(isReadOnly: true),
                m_HouseholdCitizenType = SystemAPI.GetBufferTypeHandle<HouseholdCitizen>(isReadOnly: true),
                m_TravelPurposeLookup = SystemAPI.GetComponentLookup<TravelPurpose>(isReadOnly: true),
                m_CurrentTransportLookup = SystemAPI.GetComponentLookup<CurrentTransport>(isReadOnly: true),
                m_CommandBuffer = m_EndFrameBarrier.CreateCommandBuffer().AsParallelWriter(),
                m_StatisticsQueue = m_CityStatisticsSystem.GetStatisticsEventQueue(out var deps).AsParallelWriter(),
                m_SimulationFrame = m_SimulationSystem.frameIndex,
            };
            ByeByeHomelessJob jobData = byeByeHomelessJob;
            base.Dependency = JobChunkExtensions.ScheduleParallel(jobData, m_HomelessGroup, JobHandle.CombineDependencies(base.Dependency, deps));
            m_CityStatisticsSystem.AddWriter(base.Dependency);
            m_EndFrameBarrier.AddJobHandleForProducer(base.Dependency);
        }

        public ByeByeHomelessSystem()
        {
        }
    }

    public partial class ByeByeExtraCompanySystem : GameSystemBase
    {
        [BurstCompile]
        private struct ByeByeExtraCompanyJob : IJobChunk
        {
            [ReadOnly]
            public EntityTypeHandle m_EntityType;

            public EntityCommandBuffer.ParallelWriter m_CommandBuffer;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<Entity> nativeArray = chunk.GetNativeArray(m_EntityType);
                for (int i = 0; i < nativeArray.Length; i++)
                {
                    Entity entity = nativeArray[i];
                    m_CommandBuffer.AddComponent(unfilteredChunkIndex, entity, default(Deleted));
                }
            }

            void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
            }
        }

        private EntityQuery m_ExtraCompanyGroup;

        private EndFrameBarrier m_EndFrameBarrier;

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 262144 / 32;
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_EndFrameBarrier = base.World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_ExtraCompanyGroup = GetEntityQuery(ComponentType.ReadOnly<CompanyData>(), ComponentType.Exclude<PropertyRenter>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            RequireForUpdate(m_ExtraCompanyGroup);
        }

        protected override void OnUpdate()
        {
            if (!Mod.Instance.Setting.EvictExtraCompany)
            {
                return;
            }
            ByeByeExtraCompanyJob byeByeExtraCompanyJob;
            byeByeExtraCompanyJob.m_EntityType = SystemAPI.GetEntityTypeHandle();
            byeByeExtraCompanyJob.m_CommandBuffer = m_EndFrameBarrier.CreateCommandBuffer().AsParallelWriter();
            ByeByeExtraCompanyJob jobData = byeByeExtraCompanyJob;
            base.Dependency = JobChunkExtensions.ScheduleParallel(jobData, m_ExtraCompanyGroup, base.Dependency);
            m_EndFrameBarrier.AddJobHandleForProducer(base.Dependency);
        }

        public ByeByeExtraCompanySystem()
        {
        }
    }
}