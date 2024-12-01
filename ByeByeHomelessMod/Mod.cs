using Colossal.Logging;
using Game;
using Game.Agents;
using Game.Buildings;
using Game.Citizens;
using Game.City;
using Game.Common;
using Game.Modding;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Simulation;
using Game.Tools;
using Unity.Burst.Intrinsics;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Colossal.IO.AssetDatabase;
using Colossal;
using Game.Settings;
using Game.UI;
using System.Collections.Generic;
using Game.Companies;
using Game.PSI;
using Colossal.PSI.Common;
using Colossal.Serialization.Entities;
using Colossal.Mathematics;
using Colossal.Collections;
using Game.Events;

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
            if (Game.Version.current.shortVersion != "1.1.12f1")
            {
                NotificationSystem.Push("BBH_VERSION_NOTICE", "Bye Bye Homeless", "The mod is disabled due to unsupported game version.", null, null, null, ProgressState.None, null, delegate
                {
                    var dialog = new MessageDialog("Bye Bye Homeless", "The mod has shut itself down to avoid compatibility issues. Stay tuned for further updates.\n\nIn the meantime it's safe to keep it subscribed.", "OK");
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

            if (Setting.ShowUpdateNotice20241130)
            {
                NotificationSystem.Push("BBH_UPDATE_NOTICE_20241130", "Bye Bye Homeless", "The mod is updated for game version 1.1.12f. Click to learn more.", null, null, null, ProgressState.None, null, delegate
                {
                    var dialog = new MessageDialog("Bye Bye Homeless", "The mod adjusts the game logic to minimize instances where citizens become permanently homeless.\n\nFor first-time installations or if you suspect problems with homelessness later on, use the remove-homeless buttons in the settings to clear homeless citizens.", "OK");
                    GameManager.instance.userInterface.appBindings.ShowMessageDialog(dialog, null);

                    Setting.ShowUpdateNotice20241130 = false;
                    NotificationSystem.Pop("BBH_UPDATE_NOTICE_20241130");
                });
            }

            World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<HouseholdFindPropertySystem>().Enabled = false;
            updateSystem.UpdateAt<ModifiedHouseholdFindPropertySystem>(SystemUpdatePhase.GameSimulation);

            World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<RemovedSystem>().Enabled = false;
            updateSystem.UpdateAt<ModifiedRemovedSystem>(SystemUpdatePhase.Modification5);

            World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<AccidentSiteSystem>().Enabled = false;
            updateSystem.UpdateAt<ModifiedAccidentSiteSystem>(SystemUpdatePhase.GameSimulation);

            updateSystem.UpdateAt<ByeByeHomelessSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<ByeByeExtraCompanySystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<HomelessCriminalSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<CriminalFixSystem>(SystemUpdatePhase.GameSimulation);
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
        public bool ShowUpdateNotice20241130 { get; set; }


        public bool ApplyHomelessnessFix
        {
            get
            {
                return true;
            }
            set
            {
            }
        }

        [SettingsUIButton]
        public bool DeportHomeless
        {
            set
            {
                World.DefaultGameObjectInjectionWorld?.GetOrCreateSystemManaged<ByeByeHomelessSystem>()?.DeportHomeless();
            }
        }

        [SettingsUIButton]
        public bool ArrestHomeless
        {
            set
            {
                World.DefaultGameObjectInjectionWorld?.GetOrCreateSystemManaged<ByeByeHomelessSystem>()?.ArrestHomeless();
            }
        }

        [SettingsUIButton]
        public bool RemoveStuckHomeless
        {
            set
            {
                World.DefaultGameObjectInjectionWorld?.GetOrCreateSystemManaged<ByeByeHomelessSystem>()?.DeleteStuckHomeless();
            }
        }

        [SettingsUIButton]
        public bool RemoveAllHomeless
        {
            set
            {
                World.DefaultGameObjectInjectionWorld?.GetOrCreateSystemManaged<ByeByeHomelessSystem>()?.DeleteAllHomeless();
            }
        }

        public bool EvictExtraCompany { get; set; }

        public override void SetDefaults()
        {
            ShowUpdateNotice20241130 = true;
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
                { _mSetting.GetOptionLabelLocaleID(nameof(Setting.ApplyHomelessnessFix)), "Apply homelessness bug fix" },
                { _mSetting.GetOptionDescLocaleID(nameof(Setting.ApplyHomelessnessFix)), "This is an indicator that the homelessness bug fix is applied." },
                { _mSetting.GetOptionLabelLocaleID(nameof(Setting.DeportHomeless)), "Deport Homeless" },
                { _mSetting.GetOptionDescLocaleID(nameof(Setting.DeportHomeless)), "Visa canceled! Let the campers in your parks find a way to leave the city right now." },
                { _mSetting.GetOptionLabelLocaleID(nameof(Setting.ArrestHomeless)), "Arrest Homeless" },
                { _mSetting.GetOptionDescLocaleID(nameof(Setting.ArrestHomeless)), "Send out cops to jail the homeless lifebeings." },
                { _mSetting.GetOptionLabelLocaleID(nameof(Setting.RemoveStuckHomeless)), "Remove stuck homeless" },
                { _mSetting.GetOptionDescLocaleID(nameof(Setting.RemoveStuckHomeless)), "Instantly remove homeless citizens stuck on the streets, those in parks NOT included." },
                { _mSetting.GetOptionLabelLocaleID(nameof(Setting.RemoveAllHomeless)), "Remove all homeless" },
                { _mSetting.GetOptionDescLocaleID(nameof(Setting.RemoveAllHomeless)), "Instantly remove all homeless citizens, including those in parks." },
                { _mSetting.GetOptionLabelLocaleID(nameof(Setting.EvictExtraCompany)), "Evict ghost companies" },
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


    public struct HomelessCriminal : IComponentData, IQueryTypeParameter, ISerializable
    {
        public Entity m_CrimePrefab;

        public bool m_Arrested;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(m_CrimePrefab);
            writer.Write(m_Arrested);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out m_CrimePrefab);
            reader.Read(out m_Arrested);
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
            public ComponentTypeHandle<HomelessHousehold> m_HomelessHouseholdType;

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

                NativeArray<HomelessHousehold> homelessHouseholdArray = chunk.GetNativeArray(ref m_HomelessHouseholdType);
                bool hasHomelessHousehold = homelessHouseholdArray.Length > 0;

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

                        //for (int j = 0; j < householdCitizens.Length; j++)
                        //{
                        //    Entity citizen = householdCitizens[j].m_Citizen;

                        //    if (!m_CurrentTransportLookup.TryGetComponent(citizen, out var currentTransport))
                        //    {
                        //        m_StatisticsQueue.Enqueue(new StatisticsEvent
                        //        {
                        //            m_Statistic = StatisticType.CitizensMovedAway,
                        //            m_Change = 1
                        //        });
                        //        m_CommandBuffer.AddComponent(unfilteredChunkIndex, citizen, default(Deleted));
                        //        continue;
                        //    }
                        //}
                    }
                    return;
                }

                if (!hasPropertyRenter && !hasMovingAway && !hasFindPropertyTimeout)
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

                if (!hasPropertyRenter && !hasMovingAway && hasFindPropertyTimeout)
                {
                    for (int i = 0; i < entityArray.Length; i++)
                    {
                        FindPropertyTimeout findPropertyTimeout = findPropertyTimeoutArray[i];
                        if (m_SimulationFrame - findPropertyTimeout.m_SimulationFrame >= 262144 /* one day (or month) */)
                        {
                            m_CommandBuffer.AddComponent<MovingAway>(unfilteredChunkIndex, entityArray[i]);
                            m_CommandBuffer.RemoveComponent<PropertySeeker>(unfilteredChunkIndex, entityArray[i]);
                            m_CommandBuffer.RemoveComponent<HomelessHousehold>(unfilteredChunkIndex, entityArray[i]);
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
            return 262144 / 16;
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
                m_HomelessHouseholdType = SystemAPI.GetComponentTypeHandle<HomelessHousehold>(isReadOnly: true),
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

        public void DeportHomeless()
        {
            var query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<HomelessHousehold>(), ComponentType.Exclude<MovingAway>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            base.EntityManager.AddComponent<MovingAway>(query);
            base.EntityManager.RemoveComponent<PropertySeeker>(query);
            base.EntityManager.RemoveComponent<HomelessHousehold>(query);
        }

        public void ArrestHomeless()
        {
            var query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<HomelessHousehold>(), ComponentType.ReadOnly<HouseholdCitizen>(), ComponentType.Exclude<MovingAway>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            var homelessHouseholds = query.ToEntityArray(Allocator.TempJob);
            for (int i = 0; i < homelessHouseholds.Length; i++)
            {
                var householdCitizens = base.EntityManager.GetBuffer<HouseholdCitizen>(homelessHouseholds[i], true);
                for (int j = 0; j < householdCitizens.Length; j++)
                {
                    if (base.EntityManager.HasComponent<HomelessCriminal>(householdCitizens[j]))
                    {
                        continue;
                    }

                    base.EntityManager.AddComponent<HomelessCriminal>(householdCitizens[j]);
                }
            }
            homelessHouseholds.Dispose();
        }

        public void DeleteStuckHomeless()
        {
            base.EntityManager.AddComponent<Deleted>(EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Household>(), ComponentType.Exclude<PropertyRenter>(), ComponentType.Exclude<HomelessHousehold>(), ComponentType.Exclude<CommuterHousehold>(), ComponentType.Exclude<TouristHousehold>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()));
        }

        public void DeleteAllHomeless()
        {
            DeleteStuckHomeless();
            base.EntityManager.AddComponent<Deleted>(EntityManager.CreateEntityQuery(ComponentType.ReadOnly<HomelessHousehold>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()));
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

    public partial class HomelessCriminalSystem : GameSystemBase
    {
        [BurstCompile]
        private struct HomelessCriminalJob : IJobChunk
        {
            [ReadOnly]
            public EntityTypeHandle m_EntityType;

            [ReadOnly]
            public ComponentTypeHandle<HomelessCriminal> m_HomelessCriminalType;

            [ReadOnly]
            public ComponentTypeHandle<CurrentBuilding> m_CurrentBuildingType;

            [ReadOnly]
            public ComponentTypeHandle<Criminal> m_CriminalType;

            [ReadOnly]
            public ComponentTypeHandle<Deleted> m_DeletedType;

            [ReadOnly]
            public ComponentLookup<CrimeProducer> m_CrimeProducerLookup;

            [ReadOnly]
            public ComponentLookup<Game.Buildings.PoliceStation> m_PoliceStationData;

            [ReadOnly]
            public ComponentLookup<Game.Buildings.Prison> m_PrisonData;

            [ReadOnly]
            public ComponentTypeHandle<HealthProblem> m_HealthProblemType;

            public EntityArchetype m_CrimeEventArchetype;

            public EntityCommandBuffer.ParallelWriter m_CommandBuffer;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var entities = chunk.GetNativeArray(m_EntityType);

                var homelessCriminalArray = chunk.GetNativeArray(ref m_HomelessCriminalType);

                var deletedArray = chunk.GetNativeArray(ref m_DeletedType);
                var hasDeleted = deletedArray.Length > 0;

                var currentBuildingArray = chunk.GetNativeArray(ref m_CurrentBuildingType);
                var hasCurrentBuilding = currentBuildingArray.Length > 0;

                var criminalArray = chunk.GetNativeArray(ref m_CriminalType);
                var hasCriminal = criminalArray.Length > 0;

                var healthProblemsArray = chunk.GetNativeArray(ref m_HealthProblemType);

                if (hasDeleted)
                {
                    for (var i = 0; i < entities.Length; i++)
                    {
                        var homelessCriminal = homelessCriminalArray[i];

                        if (homelessCriminal.m_CrimePrefab == Entity.Null)
                        {
                            continue;
                        }

                        m_CommandBuffer.DestroyEntity(unfilteredChunkIndex, homelessCriminal.m_CrimePrefab);
                        homelessCriminal.m_CrimePrefab = Entity.Null;
                    }

                    return;
                }

                for (var i = 0; i < entities.Length; i++)
                {
                    var entity = entities[i];
                    var homelessCriminal = homelessCriminalArray[i];

                    if (homelessCriminal.m_CrimePrefab == Entity.Null)
                    {
                        var crimeData = new CrimeData
                        {
                            m_RandomTargetType = EventTargetType.Citizen,
                            m_AlarmDelay = new Bounds1(0f, 0f),
                            m_CrimeDuration = new Bounds1(1000f, 1000f),
                            m_CrimeIncomeAbsolute = new Bounds1(0f, 0f),
                            m_CrimeIncomeRelative = new Bounds1(0f, 0f),
                            m_JailTimeRange = new Bounds1(0.125f, 1f),
                            m_PrisonTimeRange = new Bounds1(1f, 100f),
                            m_PrisonProbability = 100f,
                        };

                        var crimePrefab = m_CommandBuffer.CreateEntity(unfilteredChunkIndex);
                        m_CommandBuffer.AddComponent(unfilteredChunkIndex, crimePrefab, crimeData);

                        m_CommandBuffer.SetComponent(unfilteredChunkIndex, entity, new HomelessCriminal
                        {
                            m_CrimePrefab = crimePrefab,
                            m_Arrested = homelessCriminal.m_Arrested,
                        });

                        continue;
                    }

                    if (homelessCriminal.m_Arrested)
                    {
                        if (!hasCriminal || criminalArray[i].m_Event == Entity.Null)
                        {
                            m_CommandBuffer.DestroyEntity(unfilteredChunkIndex, homelessCriminal.m_CrimePrefab);
                            m_CommandBuffer.RemoveComponent<HomelessCriminal>(unfilteredChunkIndex, entity);

                            continue;
                        }

                        continue;
                    }

                    if (hasCriminal && (criminalArray[i].m_Flags & (CriminalFlags.Arrested | CriminalFlags.Sentenced | CriminalFlags.Prisoner)) != 0)
                    {
                        m_CommandBuffer.SetComponent(unfilteredChunkIndex, entity, new HomelessCriminal
                        {
                            m_CrimePrefab = homelessCriminal.m_CrimePrefab,
                            m_Arrested = true,
                        });

                        continue;
                    }

                    if ((!hasCriminal || criminalArray[i].m_Event == Entity.Null) && CheckHealth(healthProblemsArray, i))
                    {
                        if (hasCurrentBuilding && m_CrimeProducerLookup.HasComponent(currentBuildingArray[i].m_CurrentBuilding))
                        {
                            var crimeEvent = m_CommandBuffer.CreateEntity(unfilteredChunkIndex, m_CrimeEventArchetype);
                            m_CommandBuffer.SetComponent(unfilteredChunkIndex, crimeEvent, new PrefabRef(homelessCriminal.m_CrimePrefab));
                            var targets = m_CommandBuffer.SetBuffer<TargetElement>(unfilteredChunkIndex, crimeEvent);
                            targets.Add(new TargetElement(entity));

                            var criminal = new Criminal(crimeEvent, CriminalFlags.Robber);
                            m_CommandBuffer.AddComponent(unfilteredChunkIndex, entity, criminal);

                            m_CommandBuffer.AddComponent(unfilteredChunkIndex, entity, new TravelPurpose
                            {
                                m_Purpose = Game.Citizens.Purpose.Crime,
                            });

                            continue;
                        }

                        continue;
                    }
                }
            }

            private bool CheckHealth(NativeArray<HealthProblem> healthProblems, int index)
            {
                if (CollectionUtils.TryGet(healthProblems, index, out var value) && (value.m_Flags & HealthProblemFlags.RequireTransport) != 0)
                {
                    return false;
                }
                return true;
            }

            void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
            }
        }

        private EntityQuery m_HomelessCriminalGroup;

        private EndFrameBarrier m_EndFrameBarrier;

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            //return 262144 / 64;
            return 64;
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_EndFrameBarrier = base.World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_HomelessCriminalGroup = GetEntityQuery(ComponentType.ReadOnly<HomelessCriminal>(), ComponentType.Exclude<Temp>());
            RequireForUpdate(m_HomelessCriminalGroup);
        }

        protected override void OnUpdate()
        {
            HomelessCriminalJob job = new HomelessCriminalJob
            {
                m_EntityType = SystemAPI.GetEntityTypeHandle(),
                m_HomelessCriminalType = SystemAPI.GetComponentTypeHandle<HomelessCriminal>(isReadOnly: true),
                m_CurrentBuildingType = SystemAPI.GetComponentTypeHandle<CurrentBuilding>(isReadOnly: true),
                m_CriminalType = SystemAPI.GetComponentTypeHandle<Criminal>(isReadOnly: true),
                m_DeletedType = SystemAPI.GetComponentTypeHandle<Deleted>(isReadOnly: true),
                m_CrimeProducerLookup = SystemAPI.GetComponentLookup<CrimeProducer>(isReadOnly: true),
                m_PoliceStationData = SystemAPI.GetComponentLookup<Game.Buildings.PoliceStation>(isReadOnly: true),
                m_PrisonData = SystemAPI.GetComponentLookup<Game.Buildings.Prison>(isReadOnly: true),
                m_CommandBuffer = m_EndFrameBarrier.CreateCommandBuffer().AsParallelWriter(),
                m_CrimeEventArchetype = base.EntityManager.CreateArchetype(ComponentType.ReadWrite<Game.Events.Event>(), ComponentType.ReadWrite<Game.Events.Crime>(), ComponentType.ReadWrite<PrefabRef>(), ComponentType.ReadWrite<TargetElement>()),
                m_HealthProblemType = SystemAPI.GetComponentTypeHandle<HealthProblem>(isReadOnly: true),
            };
            base.Dependency = JobChunkExtensions.ScheduleParallel(job, m_HomelessCriminalGroup, base.Dependency);
            m_EndFrameBarrier.AddJobHandleForProducer(base.Dependency);
        }

        public HomelessCriminalSystem()
        {
        }
    }


    public partial class CriminalFixSystem : GameSystemBase
    {
        [BurstCompile]
        private struct CriminalFixJob : IJobChunk
        {
            [ReadOnly]
            public EntityTypeHandle m_EntityType;

            [ReadOnly]
            public ComponentTypeHandle<CurrentBuilding> m_CurrentBuildingType;

            [ReadOnly]
            public BufferTypeHandle<TripNeeded> m_TripNeededType;

            [ReadOnly]
            public ComponentLookup<AccidentSite> m_AccidentSiteLookup;

            [ReadOnly]
            public ComponentLookup<Game.Vehicles.PoliceCar> m_PoliceCarLookup;

            [ReadOnly]
            public ComponentLookup<Target> m_TargetLookup;

            public EntityCommandBuffer.ParallelWriter m_CommandBuffer;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var entities = chunk.GetNativeArray(m_EntityType);

                var currentBuildingArray = chunk.GetNativeArray(ref m_CurrentBuildingType);
                var hasCurrentBuilding = currentBuildingArray.Length > 0;

                var tripNeededBuffer = chunk.GetBufferAccessor(ref m_TripNeededType);
                var hasTripNeeded = tripNeededBuffer.Length > 0;

                for (int i = 0; i < entities.Length; i++)
                {
                    var entity = entities[i];

                    if (!hasCurrentBuilding || !hasTripNeeded)
                    {
                        continue;
                    }

                    var currentBuilding = currentBuildingArray[i];
                    var tripNeededs = tripNeededBuffer[i];

                    if (tripNeededs.Length == 0)
                    {
                        continue;
                    }

                    if (m_AccidentSiteLookup.HasComponent(currentBuilding.m_CurrentBuilding))
                    {
                        continue;
                    }

                    var shouldClear = false;
                    for (int j = 0; j < tripNeededs.Length; j++)
                    {
                        if (tripNeededs[j].m_Purpose == Game.Citizens.Purpose.Escape)
                        {
                            shouldClear = true;
                            break;
                        }
                    }
                    if (shouldClear)
                    {
                        m_CommandBuffer.SetBuffer<TripNeeded>(unfilteredChunkIndex, entity).Clear();
                    }
                }
            }

            void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
            }
        }

        private EntityQuery m_LostCriminalGroup;

        private EndFrameBarrier m_EndFrameBarrier;

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 64;
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_EndFrameBarrier = base.World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_LostCriminalGroup = GetEntityQuery(ComponentType.ReadOnly<Criminal>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            RequireForUpdate(m_LostCriminalGroup);
        }

        protected override void OnUpdate()
        {
            CriminalFixJob job = new CriminalFixJob
            {
                m_EntityType = SystemAPI.GetEntityTypeHandle(),
                m_CurrentBuildingType = SystemAPI.GetComponentTypeHandle<CurrentBuilding>(isReadOnly: true),
                m_TripNeededType = SystemAPI.GetBufferTypeHandle<TripNeeded>(isReadOnly: true),
                m_TargetLookup = SystemAPI.GetComponentLookup<Target>(isReadOnly: true),
                m_AccidentSiteLookup = SystemAPI.GetComponentLookup<AccidentSite>(isReadOnly: true),
                m_PoliceCarLookup = SystemAPI.GetComponentLookup<Game.Vehicles.PoliceCar>(isReadOnly: true),
                m_CommandBuffer = m_EndFrameBarrier.CreateCommandBuffer().AsParallelWriter(),
            };
            base.Dependency = JobChunkExtensions.ScheduleParallel(job, m_LostCriminalGroup, base.Dependency);
            m_EndFrameBarrier.AddJobHandleForProducer(base.Dependency);
        }

        public CriminalFixSystem()
        {
        }
    }
}