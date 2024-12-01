using System.Runtime.CompilerServices;
using Colossal.Mathematics;
using Game;
using Game.Simulation;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Events;
using Game.Net;
using Game.Notifications;
using Game.Objects;
using Game.Prefabs;
using Game.Tools;
using Game.Vehicles;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Assertions;
using UnityEngine.Scripting;

namespace ByeByeHomelessMod
{
    public partial class ModifiedAccidentSiteSystem : GameSystemBase
    {
        [BurstCompile]
        private struct AccidentSiteJob : IJobChunk
        {
            [ReadOnly]
            public EntityTypeHandle m_EntityType;

            [ReadOnly]
            public ComponentTypeHandle<Building> m_BuildingType;

            public ComponentTypeHandle<AccidentSite> m_AccidentSiteType;

            [ReadOnly]
            public ComponentLookup<InvolvedInAccident> m_InvolvedInAccidentData;

            [ReadOnly]
            public ComponentLookup<Criminal> m_CriminalData;

            [ReadOnly]
            public ComponentLookup<PoliceEmergencyRequest> m_PoliceEmergencyRequestData;

            [ReadOnly]
            public ComponentLookup<Moving> m_MovingData;

            [ReadOnly]
            public ComponentLookup<Vehicle> m_VehicleData;

            [ReadOnly]
            public ComponentLookup<Car> m_CarData;

            [ReadOnly]
            public ComponentLookup<PrefabRef> m_PrefabRefData;

            [ReadOnly]
            public ComponentLookup<TrafficAccidentData> m_PrefabTrafficAccidentData;

            [ReadOnly]
            public ComponentLookup<CrimeData> m_PrefabCrimeData;

            [ReadOnly]
            public ComponentLookup<Game.Vehicles.PoliceCar> m_PoliceCarData;

            [ReadOnly]
            public BufferLookup<TargetElement> m_TargetElements;

            [ReadOnly]
            public BufferLookup<Game.Net.SubLane> m_SubLanes;

            [ReadOnly]
            public BufferLookup<LaneObject> m_LaneObjects;

            [ReadOnly]
            public RandomSeed m_RandomSeed;

            [ReadOnly]
            public uint m_SimulationFrame;

            [ReadOnly]
            public EntityArchetype m_PoliceRequestArchetype;

            [ReadOnly]
            public EntityArchetype m_EventImpactArchetype;

            [ReadOnly]
            public PoliceConfigurationData m_PoliceConfigurationData;

            [ReadOnly]
            public ComponentLookup<Game.Citizens.CurrentTransport> m_CurrentTransportData;

            [ReadOnly]
            public ComponentLookup<Game.Creatures.CurrentVehicle> m_CurrentVehicleData;

            [ReadOnly]
            public ComponentLookup<TravelPurpose> m_TravelPurposeData;

            public EntityCommandBuffer.ParallelWriter m_CommandBuffer;

            public IconCommandBuffer m_IconCommandBuffer;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<Entity> entities = chunk.GetNativeArray(m_EntityType);
                NativeArray<AccidentSite> accidentSites = chunk.GetNativeArray(ref m_AccidentSiteType);
                bool hasBuilding = chunk.Has(ref m_BuildingType);
                for (int i = 0; i < accidentSites.Length; i++)
                {
                    Entity currentEntity = entities[i];
                    AccidentSite site = accidentSites[i];
                    Random rng = m_RandomSeed.GetRandom(currentEntity.Index);
                    Entity targetEntity = Entity.Null;
                    int involvedCount = 0;
                    float maxSeverity = 0f;
                    if (m_SimulationFrame - site.m_CreationFrame >= 3600)
                    {
                        site.m_Flags &= ~AccidentSiteFlags.StageAccident;
                    }
                    site.m_Flags &= ~AccidentSiteFlags.MovingVehicles;
                    if (m_TargetElements.HasBuffer(site.m_Event))
                    {
                        DynamicBuffer<TargetElement> targets = m_TargetElements[site.m_Event];
                        for (int j = 0; j < targets.Length; j++)
                        {
                            Entity targetElementEntity = targets[j].m_Entity;
                            if (m_InvolvedInAccidentData.TryGetComponent(targetElementEntity, out var accidentData))
                            {
                                if (accidentData.m_Event == site.m_Event)
                                {
                                    involvedCount++;
                                    bool isMoving = m_MovingData.HasComponent(targetElementEntity);
                                    if (isMoving && (site.m_Flags & AccidentSiteFlags.MovingVehicles) == 0 && m_VehicleData.HasComponent(targetElementEntity))
                                    {
                                        site.m_Flags |= AccidentSiteFlags.MovingVehicles;
                                    }
                                    if (accidentData.m_Severity > maxSeverity)
                                    {
                                        targetEntity = (isMoving ? Entity.Null : targetElementEntity);
                                        maxSeverity = accidentData.m_Severity;
                                        site.m_Flags &= ~AccidentSiteFlags.StageAccident;
                                    }
                                }
                            }
                            else
                            {
                                if (!m_CriminalData.HasComponent(targetElementEntity))
                                {
                                    continue;
                                }
                                Criminal criminal = m_CriminalData[targetElementEntity];
                                if (criminal.m_Event == site.m_Event && 
                                    ((criminal.m_Flags & CriminalFlags.Arrested) == 0 || 
                                     (!(m_CurrentTransportData.TryGetComponent(targetElementEntity, out var currentTransport) && 
                                        m_CurrentVehicleData.TryGetComponent(currentTransport.m_CurrentTransport, out var currentVehicle) && 
                                        m_PoliceCarData.HasComponent(currentVehicle.m_Vehicle)) && 
                                      m_TravelPurposeData.TryGetComponent(targetElementEntity, out var travelPurpose) && 
                                      travelPurpose.m_Purpose == Purpose.GoingToJail)))
                                {
                                    involvedCount++;
                                    if ((criminal.m_Flags & CriminalFlags.Monitored) != 0)
                                    {
                                        site.m_Flags |= AccidentSiteFlags.CrimeMonitored;
                                    }
                                }
                            }
                        }
                        if (involvedCount == 0 && (site.m_Flags & AccidentSiteFlags.StageAccident) != 0)
                        {
                            PrefabRef prefab = m_PrefabRefData[site.m_Event];
                            if (m_PrefabTrafficAccidentData.HasComponent(prefab.m_Prefab))
                            {
                                TrafficAccidentData accidentConfig = m_PrefabTrafficAccidentData[prefab.m_Prefab];
                                Entity foundSubject = TryFindSubject(currentEntity, ref rng, accidentConfig);
                                if (foundSubject != Entity.Null)
                                {
                                    AddImpact(unfilteredChunkIndex, site.m_Event, ref rng, foundSubject, accidentConfig);
                                }
                            }
                        }
                    }
                    if ((site.m_Flags & (AccidentSiteFlags.CrimeScene | AccidentSiteFlags.CrimeDetected)) == AccidentSiteFlags.CrimeScene)
                    {
                        PrefabRef crimePrefab = m_PrefabRefData[site.m_Event];
                        if (m_PrefabCrimeData.HasComponent(crimePrefab.m_Prefab))
                        {
                            CrimeData crimeConfig = m_PrefabCrimeData[crimePrefab.m_Prefab];
                            float elapsedTime = (float)(m_SimulationFrame - site.m_CreationFrame) / 60f;
                            if ((site.m_Flags & AccidentSiteFlags.CrimeMonitored) != 0 || elapsedTime >= crimeConfig.m_AlarmDelay.max)
                            {
                                site.m_Flags |= AccidentSiteFlags.CrimeDetected;
                            }
                            else if (elapsedTime >= crimeConfig.m_AlarmDelay.min)
                            {
                                float detectionChance = 1.0666667f / (crimeConfig.m_AlarmDelay.max - crimeConfig.m_AlarmDelay.min);
                                if (rng.NextFloat(1f) <= detectionChance)
                                {
                                    site.m_Flags |= AccidentSiteFlags.CrimeDetected;
                                }
                            }
                        }
                        if ((site.m_Flags & AccidentSiteFlags.CrimeDetected) != 0)
                        {
                            m_IconCommandBuffer.Add(currentEntity, m_PoliceConfigurationData.m_CrimeSceneNotificationPrefab, IconPriority.MajorProblem, IconClusterLayer.Default, IconFlags.IgnoreTarget, site.m_Event);
                        }
                    }
                    else if ((site.m_Flags & (AccidentSiteFlags.CrimeScene | AccidentSiteFlags.CrimeFinished)) == AccidentSiteFlags.CrimeScene)
                    {
                        PrefabRef crimePrefab = m_PrefabRefData[site.m_Event];
                        if (m_PrefabCrimeData.HasComponent(crimePrefab.m_Prefab))
                        {
                            CrimeData crimeConfig = m_PrefabCrimeData[crimePrefab.m_Prefab];
                            float elapsedTime = (float)(m_SimulationFrame - site.m_CreationFrame) / 60f;
                            if (elapsedTime >= crimeConfig.m_CrimeDuration.max)
                            {
                                site.m_Flags |= AccidentSiteFlags.CrimeFinished;
                            }
                            else if (elapsedTime >= crimeConfig.m_CrimeDuration.min)
                            {
                                float finishChance = 1.0666667f / (crimeConfig.m_CrimeDuration.max - crimeConfig.m_CrimeDuration.min);
                                if (rng.NextFloat(1f) <= finishChance)
                                {
                                    site.m_Flags |= AccidentSiteFlags.CrimeFinished;
                                }
                            }
                        }
                    }
                    site.m_Flags &= ~AccidentSiteFlags.RequirePolice;
                    if (maxSeverity > 0f || (site.m_Flags & (AccidentSiteFlags.Secured | AccidentSiteFlags.CrimeScene)) == AccidentSiteFlags.CrimeScene)
                    {
                        if (maxSeverity > 0f || (site.m_Flags & AccidentSiteFlags.CrimeDetected) != 0)
                        {
                            if (hasBuilding)
                            {
                                targetEntity = currentEntity;
                            }
                            if (targetEntity != Entity.Null)
                            {
                                site.m_Flags |= AccidentSiteFlags.RequirePolice;
                                RequestPoliceIfNeeded(unfilteredChunkIndex, currentEntity, ref site, targetEntity, maxSeverity);
                            }
                        }
                    }
                    else if (involvedCount == 0 && ((site.m_Flags & (AccidentSiteFlags.Secured | AccidentSiteFlags.CrimeScene)) != (AccidentSiteFlags.Secured | AccidentSiteFlags.CrimeScene) || m_SimulationFrame >= site.m_SecuredFrame + 1024))
                    {
                        m_CommandBuffer.RemoveComponent<AccidentSite>(unfilteredChunkIndex, currentEntity);
                        if ((site.m_Flags & AccidentSiteFlags.CrimeScene) != 0)
                        {
                            m_IconCommandBuffer.Remove(currentEntity, m_PoliceConfigurationData.m_CrimeSceneNotificationPrefab);
                        }
                    }
                    accidentSites[i] = site;
                }
            }

            private Entity TryFindSubject(Entity entity, ref Random random, TrafficAccidentData trafficAccidentData)
            {
                Entity result = Entity.Null;
                int num = 0;
                if (m_SubLanes.HasBuffer(entity))
                {
                    DynamicBuffer<Game.Net.SubLane> dynamicBuffer = m_SubLanes[entity];
                    for (int i = 0; i < dynamicBuffer.Length; i++)
                    {
                        Entity subLane = dynamicBuffer[i].m_SubLane;
                        if (!m_LaneObjects.HasBuffer(subLane))
                        {
                            continue;
                        }
                        DynamicBuffer<LaneObject> dynamicBuffer2 = m_LaneObjects[subLane];
                        for (int j = 0; j < dynamicBuffer2.Length; j++)
                        {
                            Entity laneObject = dynamicBuffer2[j].m_LaneObject;
                            if (trafficAccidentData.m_SubjectType == EventTargetType.MovingCar && m_CarData.HasComponent(laneObject) && m_MovingData.HasComponent(laneObject) && !m_InvolvedInAccidentData.HasComponent(laneObject))
                            {
                                num++;
                                if (random.NextInt(num) == num - 1)
                                {
                                    result = laneObject;
                                }
                            }
                        }
                    }
                }
                return result;
            }

            private void RequestPoliceIfNeeded(int jobIndex, Entity entity, ref AccidentSite accidentSite, Entity target, float severity)
            {
                if (!m_PoliceEmergencyRequestData.HasComponent(accidentSite.m_PoliceRequest))
                {
                    PolicePurpose purpose = (((accidentSite.m_Flags & AccidentSiteFlags.CrimeMonitored) == 0) ? PolicePurpose.Emergency : PolicePurpose.Intelligence);
                    Entity e = m_CommandBuffer.CreateEntity(jobIndex, m_PoliceRequestArchetype);
                    m_CommandBuffer.SetComponent(jobIndex, e, new PoliceEmergencyRequest(entity, target, severity, purpose));
                    m_CommandBuffer.SetComponent(jobIndex, e, new RequestGroup(4u));
                }
            }

            private void AddImpact(int jobIndex, Entity eventEntity, ref Random random, Entity target, TrafficAccidentData trafficAccidentData)
            {
                Impact impact = default(Impact);
                impact.m_Event = eventEntity;
                impact.m_Target = target;
                Impact component = impact;
                if (trafficAccidentData.m_AccidentType == TrafficAccidentType.LoseControl && m_MovingData.HasComponent(target))
                {
                    Moving moving = m_MovingData[target];
                    component.m_Severity = 5f;
                    if (random.NextBool())
                    {
                        component.m_AngularVelocityDelta.y = -2f;
                        component.m_VelocityDelta.xz = component.m_Severity * MathUtils.Left(math.normalizesafe(moving.m_Velocity.xz));
                    }
                    else
                    {
                        component.m_AngularVelocityDelta.y = 2f;
                        component.m_VelocityDelta.xz = component.m_Severity * MathUtils.Right(math.normalizesafe(moving.m_Velocity.xz));
                    }
                }
                Entity e = m_CommandBuffer.CreateEntity(jobIndex, m_EventImpactArchetype);
                m_CommandBuffer.SetComponent(jobIndex, e, component);
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
            public ComponentTypeHandle<Building> __Game_Buildings_Building_RO_ComponentTypeHandle;

            public ComponentTypeHandle<AccidentSite> __Game_Events_AccidentSite_RW_ComponentTypeHandle;

            [ReadOnly]
            public ComponentLookup<InvolvedInAccident> __Game_Events_InvolvedInAccident_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<Criminal> __Game_Citizens_Criminal_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<PoliceEmergencyRequest> __Game_Simulation_PoliceEmergencyRequest_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<Moving> __Game_Objects_Moving_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<Vehicle> __Game_Vehicles_Vehicle_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<Car> __Game_Vehicles_Car_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<PrefabRef> __Game_Prefabs_PrefabRef_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<TrafficAccidentData> __Game_Prefabs_TrafficAccidentData_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<CrimeData> __Game_Prefabs_CrimeData_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<Game.Vehicles.PoliceCar> __Game_Vehicles_PoliceCar_RO_ComponentLookup;

            [ReadOnly]
            public BufferLookup<TargetElement> __Game_Events_TargetElement_RO_BufferLookup;

            [ReadOnly]
            public BufferLookup<Game.Net.SubLane> __Game_Net_SubLane_RO_BufferLookup;

            [ReadOnly]
            public BufferLookup<LaneObject> __Game_Net_LaneObject_RO_BufferLookup;

            [ReadOnly]
            public ComponentLookup<Game.Citizens.CurrentTransport> __Game_Citizens_CurrentTransport_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<Game.Creatures.CurrentVehicle> __Game_Citizens_CurrentVehicle_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<TravelPurpose> __Game_Citizens_TravelPurpose_RO_ComponentLookup;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void __AssignHandles(ref SystemState state)
            {
                __Unity_Entities_Entity_TypeHandle = state.GetEntityTypeHandle();
                __Game_Buildings_Building_RO_ComponentTypeHandle = state.GetComponentTypeHandle<Building>(isReadOnly: true);
                __Game_Events_AccidentSite_RW_ComponentTypeHandle = state.GetComponentTypeHandle<AccidentSite>();
                __Game_Events_InvolvedInAccident_RO_ComponentLookup = state.GetComponentLookup<InvolvedInAccident>(isReadOnly: true);
                __Game_Citizens_Criminal_RO_ComponentLookup = state.GetComponentLookup<Criminal>(isReadOnly: true);
                __Game_Simulation_PoliceEmergencyRequest_RO_ComponentLookup = state.GetComponentLookup<PoliceEmergencyRequest>(isReadOnly: true);
                __Game_Objects_Moving_RO_ComponentLookup = state.GetComponentLookup<Moving>(isReadOnly: true);
                __Game_Vehicles_Vehicle_RO_ComponentLookup = state.GetComponentLookup<Vehicle>(isReadOnly: true);
                __Game_Vehicles_Car_RO_ComponentLookup = state.GetComponentLookup<Car>(isReadOnly: true);
                __Game_Prefabs_PrefabRef_RO_ComponentLookup = state.GetComponentLookup<PrefabRef>(isReadOnly: true);
                __Game_Prefabs_TrafficAccidentData_RO_ComponentLookup = state.GetComponentLookup<TrafficAccidentData>(isReadOnly: true);
                __Game_Prefabs_CrimeData_RO_ComponentLookup = state.GetComponentLookup<CrimeData>(isReadOnly: true);
                __Game_Vehicles_PoliceCar_RO_ComponentLookup = state.GetComponentLookup<Game.Vehicles.PoliceCar>(isReadOnly: true);
                __Game_Events_TargetElement_RO_BufferLookup = state.GetBufferLookup<TargetElement>(isReadOnly: true);
                __Game_Net_SubLane_RO_BufferLookup = state.GetBufferLookup<Game.Net.SubLane>(isReadOnly: true);
                __Game_Net_LaneObject_RO_BufferLookup = state.GetBufferLookup<LaneObject>(isReadOnly: true);
                __Game_Citizens_CurrentTransport_RO_ComponentLookup = state.GetComponentLookup<Game.Citizens.CurrentTransport>(isReadOnly: true);
                __Game_Citizens_CurrentVehicle_RO_ComponentLookup = state.GetComponentLookup<Game.Creatures.CurrentVehicle>(isReadOnly: true);
                __Game_Citizens_TravelPurpose_RO_ComponentLookup = state.GetComponentLookup<TravelPurpose>(isReadOnly: true);
            }
        }

        private const uint UPDATE_INTERVAL = 64u;

        private SimulationSystem m_SimulationSystem;

        private IconCommandSystem m_IconCommandSystem;

        private EndFrameBarrier m_EndFrameBarrier;

        private EntityQuery m_AccidentQuery;

        private EntityQuery m_ConfigQuery;

        private EntityArchetype m_PoliceRequestArchetype;

        private EntityArchetype m_EventImpactArchetype;

        private TypeHandle __TypeHandle;

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 64;
        }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_SimulationSystem = base.World.GetOrCreateSystemManaged<SimulationSystem>();
            m_IconCommandSystem = base.World.GetOrCreateSystemManaged<IconCommandSystem>();
            m_EndFrameBarrier = base.World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_AccidentQuery = GetEntityQuery(ComponentType.ReadWrite<AccidentSite>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            m_ConfigQuery = GetEntityQuery(ComponentType.ReadOnly<PoliceConfigurationData>());
            m_PoliceRequestArchetype = base.EntityManager.CreateArchetype(ComponentType.ReadWrite<ServiceRequest>(), ComponentType.ReadWrite<PoliceEmergencyRequest>(), ComponentType.ReadWrite<RequestGroup>());
            m_EventImpactArchetype = base.EntityManager.CreateArchetype(ComponentType.ReadWrite<Game.Common.Event>(), ComponentType.ReadWrite<Impact>());
            RequireForUpdate(m_AccidentQuery);
            Assert.IsTrue(condition: true);
        }

        [Preserve]
        protected override void OnUpdate()
        {
            __TypeHandle.__Game_Net_LaneObject_RO_BufferLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Net_SubLane_RO_BufferLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Events_TargetElement_RO_BufferLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Prefabs_CrimeData_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Prefabs_TrafficAccidentData_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Prefabs_PrefabRef_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Vehicles_Car_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Vehicles_Vehicle_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Objects_Moving_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Simulation_PoliceEmergencyRequest_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Citizens_Criminal_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Events_InvolvedInAccident_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Events_AccidentSite_RW_ComponentTypeHandle.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Buildings_Building_RO_ComponentTypeHandle.Update(ref base.CheckedStateRef);
            __TypeHandle.__Unity_Entities_Entity_TypeHandle.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Vehicles_PoliceCar_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Citizens_CurrentTransport_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Citizens_CurrentVehicle_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Citizens_TravelPurpose_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            AccidentSiteJob jobData = default(AccidentSiteJob);
            jobData.m_EntityType = __TypeHandle.__Unity_Entities_Entity_TypeHandle;
            jobData.m_BuildingType = __TypeHandle.__Game_Buildings_Building_RO_ComponentTypeHandle;
            jobData.m_AccidentSiteType = __TypeHandle.__Game_Events_AccidentSite_RW_ComponentTypeHandle;
            jobData.m_InvolvedInAccidentData = __TypeHandle.__Game_Events_InvolvedInAccident_RO_ComponentLookup;
            jobData.m_CriminalData = __TypeHandle.__Game_Citizens_Criminal_RO_ComponentLookup;
            jobData.m_PoliceEmergencyRequestData = __TypeHandle.__Game_Simulation_PoliceEmergencyRequest_RO_ComponentLookup;
            jobData.m_MovingData = __TypeHandle.__Game_Objects_Moving_RO_ComponentLookup;
            jobData.m_VehicleData = __TypeHandle.__Game_Vehicles_Vehicle_RO_ComponentLookup;
            jobData.m_CarData = __TypeHandle.__Game_Vehicles_Car_RO_ComponentLookup;
            jobData.m_PrefabRefData = __TypeHandle.__Game_Prefabs_PrefabRef_RO_ComponentLookup;
            jobData.m_PrefabTrafficAccidentData = __TypeHandle.__Game_Prefabs_TrafficAccidentData_RO_ComponentLookup;
            jobData.m_PrefabCrimeData = __TypeHandle.__Game_Prefabs_CrimeData_RO_ComponentLookup;
            jobData.m_PoliceCarData = __TypeHandle.__Game_Vehicles_PoliceCar_RO_ComponentLookup;
            jobData.m_TargetElements = __TypeHandle.__Game_Events_TargetElement_RO_BufferLookup;
            jobData.m_SubLanes = __TypeHandle.__Game_Net_SubLane_RO_BufferLookup;
            jobData.m_LaneObjects = __TypeHandle.__Game_Net_LaneObject_RO_BufferLookup;
            jobData.m_RandomSeed = RandomSeed.Next();
            jobData.m_SimulationFrame = m_SimulationSystem.frameIndex;
            jobData.m_PoliceRequestArchetype = m_PoliceRequestArchetype;
            jobData.m_EventImpactArchetype = m_EventImpactArchetype;
            jobData.m_PoliceConfigurationData = m_ConfigQuery.GetSingleton<PoliceConfigurationData>();
            jobData.m_CurrentTransportData = __TypeHandle.__Game_Citizens_CurrentTransport_RO_ComponentLookup;
            jobData.m_CurrentVehicleData = __TypeHandle.__Game_Citizens_CurrentVehicle_RO_ComponentLookup;
            jobData.m_TravelPurposeData = __TypeHandle.__Game_Citizens_TravelPurpose_RO_ComponentLookup;
            jobData.m_CommandBuffer = m_EndFrameBarrier.CreateCommandBuffer().AsParallelWriter();
            jobData.m_IconCommandBuffer = m_IconCommandSystem.CreateCommandBuffer();
            JobHandle jobHandle = JobChunkExtensions.ScheduleParallel(jobData, m_AccidentQuery, base.Dependency);
            m_EndFrameBarrier.AddJobHandleForProducer(jobHandle);
            m_IconCommandSystem.AddCommandBufferWriter(jobHandle);
            base.Dependency = jobHandle;
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
        public ModifiedAccidentSiteSystem()
        {
        }
    }


}