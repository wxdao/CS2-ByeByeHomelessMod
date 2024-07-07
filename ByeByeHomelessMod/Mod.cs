using System;
using System.Runtime.CompilerServices;

using Colossal.IO.AssetDatabase;
using Colossal.Logging;

using Game;
using Game.Agents;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Modding;
using Game.SceneFlow;
using Game.Tools;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

using UnityEngine.Scripting;

namespace ByeByeHomelessMod
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger($"{nameof(ByeByeHomelessMod)}.{nameof(Mod)}").SetShowsErrorsInUI(false);
        public Setting Setting { get; private set; }

        public static Mod Instance { get; private set; }

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                log.Info($"Current mod asset at {asset.path}");

            Setting = new Setting(this);
            Setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Setting));
            AssetDatabase.global.LoadSettings(nameof(Mod), Setting, new Setting(this));
            log.Info($"Homeless Cleaner will run per {Setting.TimeInterval} hour(s).");
            Instance = this;
            updateSystem.UpdateAt<ByeByeHomelessSystem>(SystemUpdatePhase.GameSimulation);
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
        }
    }

    public partial class ByeByeHomelessSystem : GameSystemBase
    {
        public static ILog log = LogManager.GetLogger($"{nameof(ByeByeHomelessMod)}.{nameof(Mod)}").SetShowsErrorsInUI(false);

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

                foreach (var entity in nativeArray)
                {
                    m_CommandBuffer.AddComponent(unfilteredChunkIndex, entity, default(Deleted));
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
            var v = (int)math.floor(10922 * Mod.Instance.Setting.TimeInterval);
            if (v < 1)
                return 1;
            v--;
            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;
            return v + 1;
        }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_HomelessGroup = GetEntityQuery(ComponentType.ReadOnly<Household>(), ComponentType.ReadOnly<HouseholdCitizen>(), ComponentType.Exclude<CommuterHousehold>(), ComponentType.Exclude<TouristHousehold>(), ComponentType.Exclude<PropertyRenter>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            RequireForUpdate(m_HomelessGroup);
        }

        [Preserve]
        protected override void OnUpdate()
        {
            __TypeHandle.__Unity_Entities_Entity_TypeHandle.Update(ref CheckedStateRef);
            __TypeHandle.__Game_Agents_MovingAway_ComponentTypeHandle.Update(ref CheckedStateRef);

            var byeByeHomelessJob = default(ByeByeHomelessJob);
            byeByeHomelessJob.m_EntityType = __TypeHandle.__Unity_Entities_Entity_TypeHandle;
            byeByeHomelessJob.m_MovingAwayType = __TypeHandle.__Game_Agents_MovingAway_ComponentTypeHandle;
            byeByeHomelessJob.m_CommandBuffer = m_EndFrameBarrier.CreateCommandBuffer().AsParallelWriter();
            var jobData = byeByeHomelessJob;
            Dependency = jobData.ScheduleParallel(m_HomelessGroup, Dependency);
            m_EndFrameBarrier.AddJobHandleForProducer(Dependency);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void __AssignQueries(ref SystemState state)
        {
        }

        protected override void OnCreateForCompiler()
        {
            base.OnCreateForCompiler();
            __AssignQueries(ref CheckedStateRef);
            __TypeHandle.__AssignHandles(ref CheckedStateRef);
        }

        [Preserve]
        public ByeByeHomelessSystem()
        {
        }
    }
}