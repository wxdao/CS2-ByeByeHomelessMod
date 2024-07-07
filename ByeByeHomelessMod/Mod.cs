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
            Setting.RegisterInOptionsUI();

            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Setting));

            AssetDatabase.global.LoadSettings(nameof(Mod), Setting, new Setting(this));

            updateSystem.UpdateAt<ByeByeHomelessSystem>(SystemUpdatePhase.GameSimulation);
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
        }
    }

    [FileLocation("ModSettings/ByeByeHomeless/setting.coc")]
    public class Setting : ModSetting
    {
        public Setting(IMod mod) : base(mod)
        {
            SetDefaults();
        }

        public bool ActionDelete { get; set; }

        public enum TickIntervalOptions
        {
            M45,
            M90,
            M180,
        }

        public TickIntervalOptions TickInterval { get; set; }

        public override void SetDefaults()
        {
            ActionDelete = false;
            TickInterval = TickIntervalOptions.M90;
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
                { _mSetting.GetOptionLabelLocaleID(nameof(Setting.ActionDelete)), "Delete Instead of Move" },
                { _mSetting.GetOptionDescLocaleID(nameof(Setting.ActionDelete)), "When checked, stuck homeless households will be deleted instead of being forced to move out of the city." },
                { _mSetting.GetOptionLabelLocaleID(nameof(Setting.TickInterval)), "Eviction Period" },
                { _mSetting.GetOptionDescLocaleID(nameof(Setting.TickInterval)), "The maximum amount of time the homeless have to find a new house or shelter before being evicted. Requires a restart to take effect." },
                { _mSetting.GetEnumValueLocaleID(TickIntervalOptions.M45), "45 in-game minutes" },
                { _mSetting.GetEnumValueLocaleID(TickIntervalOptions.M90), "90 in-game minutes" },
                { _mSetting.GetEnumValueLocaleID(TickIntervalOptions.M180), "180 in-game minutes" },
            };
        }

        public void Unload()
        {
        }
    }

    public partial class ByeByeHomelessSystem : GameSystemBase
    {
        private struct ByeByeHomelessJob : IJobChunk
        {
            [ReadOnly]
            public EntityTypeHandle m_EntityType;

            [ReadOnly]
            public ComponentTypeHandle<MovingAway> m_MovingAwayType;

            public EntityCommandBuffer.ParallelWriter m_CommandBuffer;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<Entity> nativeArray = chunk.GetNativeArray(m_EntityType);

                if (Mod.Instance.Setting.ActionDelete)
                {
                    for (int i = 0; i < nativeArray.Length; i++)
                    {
                        Entity entity = nativeArray[i];
                        m_CommandBuffer.AddComponent(unfilteredChunkIndex, entity, default(Deleted));
                    }
                    return;
                }

                if (chunk.Has(ref m_MovingAwayType))
                {
                    NativeArray<MovingAway> nativeArray2 = chunk.GetNativeArray(ref m_MovingAwayType);
                    for (int i = 0; i < nativeArray.Length; i++)
                    {
                        Entity entity = nativeArray[i];
                        MovingAway movingAway = nativeArray2[i];
                        if (movingAway.m_Target == Entity.Null)
                        {
                            m_CommandBuffer.AddComponent(unfilteredChunkIndex, entity, default(Deleted));
                        }
                    }
                    return;
                }

                for (int i = 0; i < nativeArray.Length; i++)
                {
                    Entity entity = nativeArray[i];
                    m_CommandBuffer.AddComponent(unfilteredChunkIndex, entity, default(MovingAway));
                    m_CommandBuffer.RemoveComponent<PropertySeeker>(unfilteredChunkIndex, entity);
                }
            }

            void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
            }
        }

        private struct TypeHandle
        {
            [ReadOnly]
            public EntityTypeHandle __Unity_Entities_Entity_TypeHandle;

            [ReadOnly]
            public ComponentTypeHandle<MovingAway> __Game_Agents_MovingAway_ComponentTypeHandle;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void __AssignHandles(ref SystemState state)
            {
                __Unity_Entities_Entity_TypeHandle = state.GetEntityTypeHandle();
                __Game_Agents_MovingAway_ComponentTypeHandle = state.GetComponentTypeHandle<MovingAway>();
            }
        }

        private EntityQuery m_HomelessGroup;

        private EndFrameBarrier m_EndFrameBarrier;

        private TypeHandle __TypeHandle;

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            switch (Mod.Instance.Setting.TickInterval)
            {
                case TickIntervalOptions.M45:
                    return 262144 / 32;
                case TickIntervalOptions.M90:
                default:
                    return 262144 / 16;
                case TickIntervalOptions.M180:
                    return 262144 / 8;
            }
        }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_EndFrameBarrier = base.World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_HomelessGroup = GetEntityQuery(ComponentType.ReadOnly<Household>(), ComponentType.ReadOnly<HouseholdCitizen>(), ComponentType.Exclude<CommuterHousehold>(), ComponentType.Exclude<TouristHousehold>(), ComponentType.Exclude<PropertyRenter>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            RequireForUpdate(m_HomelessGroup);
        }

        [Preserve]
        protected override void OnUpdate()
        {
            __TypeHandle.__Unity_Entities_Entity_TypeHandle.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Agents_MovingAway_ComponentTypeHandle.Update(ref base.CheckedStateRef);

            ByeByeHomelessJob byeByeHomelessJob = default(ByeByeHomelessJob);
            byeByeHomelessJob.m_EntityType = __TypeHandle.__Unity_Entities_Entity_TypeHandle;
            byeByeHomelessJob.m_MovingAwayType = __TypeHandle.__Game_Agents_MovingAway_ComponentTypeHandle;
            byeByeHomelessJob.m_CommandBuffer = m_EndFrameBarrier.CreateCommandBuffer().AsParallelWriter();
            ByeByeHomelessJob jobData = byeByeHomelessJob;
            base.Dependency = JobChunkExtensions.ScheduleParallel(jobData, m_HomelessGroup, base.Dependency);
            m_EndFrameBarrier.AddJobHandleForProducer(base.Dependency);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void __AssignQueries(ref SystemState state)
        {
        }

        protected override void OnCreateForCompiler()
        {
            base.OnCreateForCompiler();
            __AssignQueries(ref base.CheckedStateRef);
            __TypeHandle.__AssignHandles(ref base.CheckedStateRef);
        }

        [Preserve]
        public ByeByeHomelessSystem()
        {
        }
    }
}