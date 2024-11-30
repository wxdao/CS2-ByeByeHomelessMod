using System.Runtime.CompilerServices;
using Game;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Burst.Intrinsics;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine.Scripting;
using Game.Companies;
using Game.Notifications;

namespace ByeByeHomelessMod
{
    public partial class ModifiedRemovedSystem : GameSystemBase

    {
        [BurstCompile]
        private struct RemovedPropertyJob : IJobChunk
        {
            [ReadOnly]
            public EntityTypeHandle m_EntityType;

            [ReadOnly]
            public BufferTypeHandle<Renter> m_RenterType;

            [ReadOnly]
            public ComponentLookup<PropertyRenter> m_PropertyRenters;

            public EntityCommandBuffer.ParallelWriter m_CommandBuffer;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                BufferAccessor<Renter> bufferAccessor = chunk.GetBufferAccessor(ref m_RenterType);
                for (int i = 0; i < bufferAccessor.Length; i++)
                {
                    DynamicBuffer<Renter> dynamicBuffer = bufferAccessor[i];
                    for (int j = 0; j < dynamicBuffer.Length; j++)
                    {
                        if (m_PropertyRenters.HasComponent(dynamicBuffer[j].m_Renter))
                        {
                            m_CommandBuffer.RemoveComponent<PropertyRenter>(unfilteredChunkIndex, dynamicBuffer[j].m_Renter);
                        }
                    }
                }
            }

            void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
            }
        }

        [BurstCompile]
        private struct RemovedWorkplaceJob : IJobChunk
        {
            [ReadOnly]
            public EntityTypeHandle m_EntityType;

            [ReadOnly]
            public BufferTypeHandle<Employee> m_EmployeeType;

            [ReadOnly]
            public ComponentLookup<TravelPurpose> m_Purposes;

            [ReadOnly]
            public ComponentLookup<Worker> m_Workers;

            public EntityCommandBuffer.ParallelWriter m_CommandBuffer;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<Entity> nativeArray = chunk.GetNativeArray(m_EntityType);
                for (int i = 0; i < nativeArray.Length; i++)
                {
                    m_CommandBuffer.RemoveComponent<FreeWorkplaces>(unfilteredChunkIndex, nativeArray[i]);
                }
                BufferAccessor<Employee> bufferAccessor = chunk.GetBufferAccessor(ref m_EmployeeType);
                for (int j = 0; j < bufferAccessor.Length; j++)
                {
                    DynamicBuffer<Employee> dynamicBuffer = bufferAccessor[j];
                    for (int k = 0; k < dynamicBuffer.Length; k++)
                    {
                        Entity worker = dynamicBuffer[k].m_Worker;
                        if (m_Purposes.HasComponent(worker) && (m_Purposes[worker].m_Purpose == Game.Citizens.Purpose.GoingToWork || m_Purposes[worker].m_Purpose == Game.Citizens.Purpose.Working))
                        {
                            m_CommandBuffer.RemoveComponent<TravelPurpose>(unfilteredChunkIndex, worker);
                        }
                        if (m_Workers.HasComponent(worker))
                        {
                            m_CommandBuffer.RemoveComponent<Worker>(unfilteredChunkIndex, worker);
                        }
                    }
                }
            }

            void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
            }
        }

        [BurstCompile]
        private struct RemovedCompanyJob : IJobChunk
        {
            [ReadOnly]
            public ComponentTypeHandle<CompanyNotifications> m_NotificationsType;

            public IconCommandBuffer m_IconCommandBuffer;

            public CompanyNotificationParameterData m_CompanyNotificationParameters;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<CompanyNotifications> nativeArray = chunk.GetNativeArray(ref m_NotificationsType);
                for (int i = 0; i < nativeArray.Length; i++)
                {
                    CompanyNotifications companyNotifications = nativeArray[i];
                    if (companyNotifications.m_NoCustomersEntity != default(Entity))
                    {
                        m_IconCommandBuffer.Remove(companyNotifications.m_NoCustomersEntity, m_CompanyNotificationParameters.m_NoCustomersNotificationPrefab);
                    }
                    if (companyNotifications.m_NoInputEntity != default(Entity))
                    {
                        m_IconCommandBuffer.Remove(companyNotifications.m_NoInputEntity, m_CompanyNotificationParameters.m_NoInputsNotificationPrefab);
                    }
                }
            }

            void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
            }
        }

        [BurstCompile]
        private struct RentersUpdateJob : IJobChunk
        {
            [ReadOnly]
            public ComponentTypeHandle<RentersUpdated> m_RentersUpdatedType;

            public BufferLookup<Renter> m_Renters;

            [ReadOnly]
            public ComponentLookup<PropertyRenter> m_PropertyRenters;

            [ReadOnly]
            public ComponentLookup<HomelessHousehold> m_HomelessHouseholds;

            public ComponentLookup<Building> m_Buildings;

            [ReadOnly]
            public BuildingConfigurationData m_BuildingConfigurationData;

            public IconCommandBuffer m_IconCommandBuffer;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<RentersUpdated> nativeArray = chunk.GetNativeArray(ref m_RentersUpdatedType);
                for (int i = 0; i < chunk.Count; i++)
                {
                    RentersUpdated rentersUpdated = nativeArray[i];
                    if (!m_Buildings.TryGetComponent(rentersUpdated.m_Property, out var componentData) || !m_Renters.TryGetBuffer(rentersUpdated.m_Property, out var bufferData))
                    {
                        continue;
                    }
                    for (int num = bufferData.Length - 1; num >= 0; num--)
                    {
                        var hasPropertyRenter = m_PropertyRenters.TryGetComponent(bufferData[num].m_Renter, out var propertyRenter);
                        var hasHomelessHousehold = m_HomelessHouseholds.TryGetComponent(bufferData[num].m_Renter, out var homelessHousehold);

                        if ((!hasPropertyRenter || propertyRenter.m_Property != rentersUpdated.m_Property) && (!hasHomelessHousehold || homelessHousehold.m_TempHome != rentersUpdated.m_Property))
                        {
                            bufferData.RemoveAt(num);
                            continue;
                        }
                    }
                    if ((componentData.m_Flags & Game.Buildings.BuildingFlags.HighRentWarning) != 0 && bufferData.Length == 0)
                    {
                        m_IconCommandBuffer.Remove(rentersUpdated.m_Property, m_BuildingConfigurationData.m_HighRentNotification);
                        componentData.m_Flags &= ~Game.Buildings.BuildingFlags.HighRentWarning;
                        m_Buildings[rentersUpdated.m_Property] = componentData;
                    }
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
            public BufferTypeHandle<Renter> __Game_Buildings_Renter_RO_BufferTypeHandle;

            [ReadOnly]
            public ComponentLookup<PropertyRenter> __Game_Buildings_PropertyRenter_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<HomelessHousehold> __Game_Citizens_HomelessHousehold_RO_ComponentLookup;

            [ReadOnly]
            public BufferTypeHandle<Employee> __Game_Companies_Employee_RO_BufferTypeHandle;

            [ReadOnly]
            public ComponentLookup<TravelPurpose> __Game_Citizens_TravelPurpose_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<Worker> __Game_Citizens_Worker_RO_ComponentLookup;

            [ReadOnly]
            public ComponentTypeHandle<CompanyNotifications> __Game_Companies_CompanyNotifications_RO_ComponentTypeHandle;

            public ComponentTypeHandle<RentersUpdated> __Game_Buildings_RentersUpdated_RW_ComponentTypeHandle;

            public BufferLookup<Renter> __Game_Buildings_Renter_RW_BufferLookup;

            public ComponentLookup<Building> __Game_Buildings_Building_RW_ComponentLookup;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void __AssignHandles(ref SystemState state)
            {
                __Unity_Entities_Entity_TypeHandle = state.GetEntityTypeHandle();
                __Game_Buildings_Renter_RO_BufferTypeHandle = state.GetBufferTypeHandle<Renter>(isReadOnly: true);
                __Game_Buildings_PropertyRenter_RO_ComponentLookup = state.GetComponentLookup<PropertyRenter>(isReadOnly: true);
                __Game_Citizens_HomelessHousehold_RO_ComponentLookup = state.GetComponentLookup<HomelessHousehold>(isReadOnly: true);
                __Game_Companies_Employee_RO_BufferTypeHandle = state.GetBufferTypeHandle<Employee>(isReadOnly: true);
                __Game_Citizens_TravelPurpose_RO_ComponentLookup = state.GetComponentLookup<TravelPurpose>(isReadOnly: true);
                __Game_Citizens_Worker_RO_ComponentLookup = state.GetComponentLookup<Worker>(isReadOnly: true);
                __Game_Companies_CompanyNotifications_RO_ComponentTypeHandle = state.GetComponentTypeHandle<CompanyNotifications>(isReadOnly: true);
                __Game_Buildings_RentersUpdated_RW_ComponentTypeHandle = state.GetComponentTypeHandle<RentersUpdated>();
                __Game_Buildings_Renter_RW_BufferLookup = state.GetBufferLookup<Renter>();
                __Game_Buildings_Building_RW_ComponentLookup = state.GetComponentLookup<Building>();
            }
        }

        private EntityQuery m_DeletedBuildings;

        private EntityQuery m_DeletedWorkplaces;

        private EntityQuery m_DeletedCompanies;

        private EntityQuery m_NeedUpdateRenterQuery;

        private EntityQuery m_BuildingParameterQuery;

        private EntityQuery m_CompanyNotificationParameterQuery;

        private IconCommandSystem m_IconCommandSystem;

        private ModificationBarrier5 m_ModificationBarrier;

        private TypeHandle __TypeHandle;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_ModificationBarrier = base.World.GetOrCreateSystemManaged<ModificationBarrier5>();
            m_IconCommandSystem = base.World.GetOrCreateSystemManaged<IconCommandSystem>();
            m_DeletedBuildings = GetEntityQuery(ComponentType.ReadOnly<Renter>(), ComponentType.ReadOnly<Deleted>(), ComponentType.Exclude<Temp>());
            m_DeletedWorkplaces = GetEntityQuery(ComponentType.ReadOnly<Employee>(), ComponentType.ReadOnly<Deleted>(), ComponentType.Exclude<Temp>());
            m_DeletedCompanies = GetEntityQuery(ComponentType.ReadOnly<CompanyNotifications>(), ComponentType.ReadOnly<Deleted>(), ComponentType.Exclude<Temp>());
            m_NeedUpdateRenterQuery = GetEntityQuery(ComponentType.ReadOnly<Game.Common.Event>(), ComponentType.ReadOnly<RentersUpdated>());
            m_BuildingParameterQuery = GetEntityQuery(ComponentType.ReadOnly<BuildingConfigurationData>());
            m_CompanyNotificationParameterQuery = GetEntityQuery(ComponentType.ReadOnly<CompanyNotificationParameterData>());
        }

        [Preserve]
        protected override void OnUpdate()
        {
            JobHandle jobHandle = default(JobHandle);
            if (!m_DeletedBuildings.IsEmptyIgnoreFilter)
            {
                __TypeHandle.__Game_Buildings_PropertyRenter_RO_ComponentLookup.Update(ref base.CheckedStateRef);
                __TypeHandle.__Game_Buildings_Renter_RO_BufferTypeHandle.Update(ref base.CheckedStateRef);
                __TypeHandle.__Unity_Entities_Entity_TypeHandle.Update(ref base.CheckedStateRef);
                RemovedPropertyJob jobData = default(RemovedPropertyJob);
                jobData.m_EntityType = __TypeHandle.__Unity_Entities_Entity_TypeHandle;
                jobData.m_RenterType = __TypeHandle.__Game_Buildings_Renter_RO_BufferTypeHandle;
                jobData.m_PropertyRenters = __TypeHandle.__Game_Buildings_PropertyRenter_RO_ComponentLookup;
                jobData.m_CommandBuffer = m_ModificationBarrier.CreateCommandBuffer().AsParallelWriter();
                jobHandle = JobChunkExtensions.ScheduleParallel(jobData, m_DeletedBuildings, base.Dependency);
                m_ModificationBarrier.AddJobHandleForProducer(jobHandle);
            }
            JobHandle jobHandle2 = default(JobHandle);
            if (!m_DeletedWorkplaces.IsEmptyIgnoreFilter)
            {
                __TypeHandle.__Game_Citizens_Worker_RO_ComponentLookup.Update(ref base.CheckedStateRef);
                __TypeHandle.__Game_Citizens_TravelPurpose_RO_ComponentLookup.Update(ref base.CheckedStateRef);
                __TypeHandle.__Game_Companies_Employee_RO_BufferTypeHandle.Update(ref base.CheckedStateRef);
                __TypeHandle.__Unity_Entities_Entity_TypeHandle.Update(ref base.CheckedStateRef);
                RemovedWorkplaceJob jobData2 = default(RemovedWorkplaceJob);
                jobData2.m_EntityType = __TypeHandle.__Unity_Entities_Entity_TypeHandle;
                jobData2.m_EmployeeType = __TypeHandle.__Game_Companies_Employee_RO_BufferTypeHandle;
                jobData2.m_Purposes = __TypeHandle.__Game_Citizens_TravelPurpose_RO_ComponentLookup;
                jobData2.m_Workers = __TypeHandle.__Game_Citizens_Worker_RO_ComponentLookup;
                jobData2.m_CommandBuffer = m_ModificationBarrier.CreateCommandBuffer().AsParallelWriter();
                jobHandle2 = JobChunkExtensions.ScheduleParallel(jobData2, m_DeletedWorkplaces, base.Dependency);
                m_ModificationBarrier.AddJobHandleForProducer(jobHandle2);
            }
            JobHandle jobHandle3 = default(JobHandle);
            if (!m_DeletedCompanies.IsEmptyIgnoreFilter && !m_CompanyNotificationParameterQuery.IsEmptyIgnoreFilter)
            {
                __TypeHandle.__Game_Companies_CompanyNotifications_RO_ComponentTypeHandle.Update(ref base.CheckedStateRef);
                RemovedCompanyJob jobData3 = default(RemovedCompanyJob);
                jobData3.m_NotificationsType = __TypeHandle.__Game_Companies_CompanyNotifications_RO_ComponentTypeHandle;
                jobData3.m_CompanyNotificationParameters = m_CompanyNotificationParameterQuery.GetSingleton<CompanyNotificationParameterData>();
                jobData3.m_IconCommandBuffer = m_IconCommandSystem.CreateCommandBuffer();
                jobHandle3 = JobChunkExtensions.ScheduleParallel(jobData3, m_DeletedCompanies, base.Dependency);
                m_IconCommandSystem.AddCommandBufferWriter(jobHandle3);
            }
            JobHandle jobHandle4 = default(JobHandle);
            if (!m_NeedUpdateRenterQuery.IsEmptyIgnoreFilter)
            {
                __TypeHandle.__Game_Buildings_Building_RW_ComponentLookup.Update(ref base.CheckedStateRef);
                __TypeHandle.__Game_Buildings_PropertyRenter_RO_ComponentLookup.Update(ref base.CheckedStateRef);
                __TypeHandle.__Game_Citizens_HomelessHousehold_RO_ComponentLookup.Update(ref base.CheckedStateRef);
                __TypeHandle.__Game_Buildings_Renter_RW_BufferLookup.Update(ref base.CheckedStateRef);
                __TypeHandle.__Game_Buildings_RentersUpdated_RW_ComponentTypeHandle.Update(ref base.CheckedStateRef);
                RentersUpdateJob jobData4 = default(RentersUpdateJob);
                jobData4.m_RentersUpdatedType = __TypeHandle.__Game_Buildings_RentersUpdated_RW_ComponentTypeHandle;
                jobData4.m_Renters = __TypeHandle.__Game_Buildings_Renter_RW_BufferLookup;
                jobData4.m_PropertyRenters = __TypeHandle.__Game_Buildings_PropertyRenter_RO_ComponentLookup;
                jobData4.m_HomelessHouseholds = __TypeHandle.__Game_Citizens_HomelessHousehold_RO_ComponentLookup;
                jobData4.m_Buildings = __TypeHandle.__Game_Buildings_Building_RW_ComponentLookup;
                jobData4.m_BuildingConfigurationData = m_BuildingParameterQuery.GetSingleton<BuildingConfigurationData>();
                jobData4.m_IconCommandBuffer = m_IconCommandSystem.CreateCommandBuffer();
                jobHandle4 = JobChunkExtensions.Schedule(jobData4, m_NeedUpdateRenterQuery, JobHandle.CombineDependencies(base.Dependency, jobHandle));
                m_IconCommandSystem.AddCommandBufferWriter(jobHandle4);
            }
            base.Dependency = JobHandle.CombineDependencies(jobHandle, jobHandle2, JobHandle.CombineDependencies(jobHandle3, jobHandle4));
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
        public ModifiedRemovedSystem()
        {
        }
    }

}