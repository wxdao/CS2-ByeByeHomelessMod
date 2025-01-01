using System.Runtime.CompilerServices;
using Game;
using Game.Agents;
using Game.Buildings;
using Game.Citizens;
using Game.City;
using Game.Common;
using Game.Economy;
using Game.Prefabs;
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
using Game.Companies;
using Colossal.Entities;
using Game.Areas;
using Game.Debug;
using Game.Net;
using Game.Objects;
using Game.Pathfind;
using static Game.Simulation.HouseholdFindPropertySystem;
using static Game.Buildings.PropertyUtils;

namespace ByeByeHomelessMod
{
    public partial class ModifiedHouseholdFindPropertySystem : GameSystemBase
    {

        [BurstCompile]
        private struct PreparePropertyJob : IJobChunk
        {
            [ReadOnly]
            public EntityTypeHandle m_EntityType;

            [ReadOnly]
            public ComponentLookup<BuildingPropertyData> m_BuildingProperties;

            [ReadOnly]
            public ComponentLookup<PrefabRef> m_Prefabs;

            [ReadOnly]
            public BufferLookup<Renter> m_Renters;

            [ReadOnly]
            public ComponentLookup<Abandoned> m_Abandoneds;

            [ReadOnly]
            public ComponentLookup<Game.Buildings.Park> m_Parks;

            [ReadOnly]
            public ComponentLookup<BuildingData> m_BuildingDatas;

            [ReadOnly]
            public ComponentLookup<ParkData> m_ParkDatas;

            [ReadOnly]
            public ComponentLookup<Household> m_Households;

            [ReadOnly]
            public ComponentLookup<Building> m_Buildings;

            [ReadOnly]
            public ComponentLookup<SpawnableBuildingData> m_SpawnableDatas;

            [ReadOnly]
            public ComponentLookup<BuildingPropertyData> m_BuildingPropertyData;

            [ReadOnly]
            public BufferLookup<Game.Net.ServiceCoverage> m_ServiceCoverages;

            [ReadOnly]
            public ComponentLookup<CrimeProducer> m_Crimes;

            [ReadOnly]
            public ComponentLookup<Game.Objects.Transform> m_Transforms;

            [ReadOnly]
            public ComponentLookup<Locked> m_Locked;

            [ReadOnly]
            public BufferLookup<CityModifier> m_CityModifiers;

            [ReadOnly]
            public ComponentLookup<ElectricityConsumer> m_ElectricityConsumers;

            [ReadOnly]
            public ComponentLookup<WaterConsumer> m_WaterConsumers;

            [ReadOnly]
            public ComponentLookup<GarbageProducer> m_GarbageProducers;

            [ReadOnly]
            public ComponentLookup<MailProducer> m_MailProducers;

            [ReadOnly]
            public NativeArray<AirPollution> m_AirPollutionMap;

            [ReadOnly]
            public NativeArray<GroundPollution> m_PollutionMap;

            [ReadOnly]
            public NativeArray<NoisePollution> m_NoiseMap;

            [ReadOnly]
            public CellMapData<TelecomCoverage> m_TelecomCoverages;

            public HealthcareParameterData m_HealthcareParameters;

            public ParkParameterData m_ParkParameters;

            public EducationParameterData m_EducationParameters;

            public TelecomParameterData m_TelecomParameters;

            public GarbageParameterData m_GarbageParameters;

            public PoliceConfigurationData m_PoliceParameters;

            public CitizenHappinessParameterData m_CitizenHappinessParameterData;

            public Entity m_City;

            public NativeParallelHashMap<Entity, CachedPropertyInformation>.ParallelWriter m_PropertyData;

            private int CalculateFree(Entity property)
            {
                Entity prefab = m_Prefabs[property].m_Prefab;
                int num = 0;
                if (m_BuildingDatas.HasComponent(prefab) && (m_Abandoneds.HasComponent(property) || (m_Parks.HasComponent(property) && m_ParkDatas[prefab].m_AllowHomeless)))
                {
                    num = HomelessShelterAISystem.GetShelterCapacity(m_BuildingDatas[prefab], m_BuildingPropertyData.HasComponent(prefab) ? m_BuildingPropertyData[prefab] : default(BuildingPropertyData)) - m_Renters[property].Length;
                }
                else if (m_BuildingProperties.HasComponent(prefab))
                {
                    BuildingPropertyData buildingPropertyData = m_BuildingProperties[prefab];
                    DynamicBuffer<Renter> dynamicBuffer = m_Renters[property];
                    num = buildingPropertyData.CountProperties(Game.Zones.AreaType.Residential);
                    for (int i = 0; i < dynamicBuffer.Length; i++)
                    {
                        Entity renter = dynamicBuffer[i].m_Renter;
                        if (m_Households.HasComponent(renter))
                        {
                            num--;
                        }
                    }
                }
                return num;
            }

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<Entity> nativeArray = chunk.GetNativeArray(m_EntityType);
                for (int i = 0; i < nativeArray.Length; i++)
                {
                    Entity entity = nativeArray[i];
                    int num = CalculateFree(entity);
                    if (num > 0)
                    {
                        Entity prefab = m_Prefabs[entity].m_Prefab;
                        Building buildingData = m_Buildings[entity];
                        Entity healthcareServicePrefab = m_HealthcareParameters.m_HealthcareServicePrefab;
                        Entity parkServicePrefab = m_ParkParameters.m_ParkServicePrefab;
                        Entity educationServicePrefab = m_EducationParameters.m_EducationServicePrefab;
                        Entity telecomServicePrefab = m_TelecomParameters.m_TelecomServicePrefab;
                        Entity garbageServicePrefab = m_GarbageParameters.m_GarbageServicePrefab;
                        Entity policeServicePrefab = m_PoliceParameters.m_PoliceServicePrefab;
                        DynamicBuffer<CityModifier> cityModifiers = m_CityModifiers[m_City];
                        GenericApartmentQuality genericApartmentQuality = PropertyUtils.GetGenericApartmentQuality(entity, prefab, ref buildingData, ref m_BuildingProperties, ref m_BuildingDatas, ref m_SpawnableDatas, ref m_Crimes, ref m_ServiceCoverages, ref m_Locked, ref m_ElectricityConsumers, ref m_WaterConsumers, ref m_GarbageProducers, ref m_MailProducers, ref m_Transforms, ref m_Abandoneds, m_PollutionMap, m_AirPollutionMap, m_NoiseMap, m_TelecomCoverages, cityModifiers, healthcareServicePrefab, parkServicePrefab, educationServicePrefab, telecomServicePrefab, garbageServicePrefab, policeServicePrefab, m_CitizenHappinessParameterData, m_GarbageParameters);
                        m_PropertyData.TryAdd(entity, new CachedPropertyInformation
                        {
                            free = num,
                            quality = genericApartmentQuality
                        });
                    }
                }
            }

            void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
            }
        }

        [BurstCompile]
        private struct FindPropertyJob : IJob
        {
            public NativeList<Entity> m_Entities;

            public NativeParallelHashMap<Entity, CachedPropertyInformation> m_CachedPropertyInfo;

            [ReadOnly]
            public ComponentLookup<BuildingData> m_BuildingDatas;

            [ReadOnly]
            public ComponentLookup<PropertyOnMarket> m_PropertiesOnMarket;

            [ReadOnly]
            public BufferLookup<PathInformations> m_PathInformationBuffers;

            [ReadOnly]
            public ComponentLookup<PrefabRef> m_PrefabRefs;

            [ReadOnly]
            public ComponentLookup<Building> m_Buildings;

            [ReadOnly]
            public ComponentLookup<Worker> m_Workers;

            [ReadOnly]
            public ComponentLookup<Game.Citizens.Student> m_Students;

            [ReadOnly]
            public ComponentLookup<BuildingPropertyData> m_BuildingProperties;

            [ReadOnly]
            public ComponentLookup<SpawnableBuildingData> m_SpawnableDatas;

            [ReadOnly]
            public ComponentLookup<PropertyRenter> m_PropertyRenters;

            [ReadOnly]
            public BufferLookup<ResourceAvailability> m_Availabilities;

            [ReadOnly]
            public BufferLookup<Game.Net.ServiceCoverage> m_ServiceCoverages;

            [ReadOnly]
            public ComponentLookup<HomelessHousehold> m_HomelessHouseholds;

            [ReadOnly]
            public ComponentLookup<Citizen> m_Citizens;

            [ReadOnly]
            public ComponentLookup<CrimeProducer> m_Crimes;

            [ReadOnly]
            public ComponentLookup<Game.Objects.Transform> m_Transforms;

            [ReadOnly]
            public ComponentLookup<Locked> m_Lockeds;

            [ReadOnly]
            public BufferLookup<CityModifier> m_CityModifiers;

            [ReadOnly]
            public ComponentLookup<HealthProblem> m_HealthProblems;

            [ReadOnly]
            public ComponentLookup<Game.Buildings.Park> m_Parks;

            [ReadOnly]
            public ComponentLookup<Abandoned> m_Abandoneds;

            [ReadOnly]
            public BufferLookup<OwnedVehicle> m_OwnedVehicles;

            [ReadOnly]
            public ComponentLookup<ElectricityConsumer> m_ElectricityConsumers;

            [ReadOnly]
            public ComponentLookup<WaterConsumer> m_WaterConsumers;

            [ReadOnly]
            public ComponentLookup<GarbageProducer> m_GarbageProducers;

            [ReadOnly]
            public ComponentLookup<MailProducer> m_MailProducers;

            [ReadOnly]
            public ComponentLookup<Household> m_Households;

            [ReadOnly]
            public ComponentLookup<CurrentBuilding> m_CurrentBuildings;

            [ReadOnly]
            public ComponentLookup<CurrentTransport> m_CurrentTransports;

            [ReadOnly]
            public BufferLookup<HouseholdCitizen> m_CitizenBuffers;

            public ComponentLookup<PropertySeeker> m_PropertySeekers;

            [ReadOnly]
            public NativeArray<AirPollution> m_AirPollutionMap;

            [ReadOnly]
            public NativeArray<GroundPollution> m_PollutionMap;

            [ReadOnly]
            public NativeArray<NoisePollution> m_NoiseMap;

            [ReadOnly]
            public CellMapData<TelecomCoverage> m_TelecomCoverages;

            [ReadOnly]
            public ResourcePrefabs m_ResourcePrefabs;

            [ReadOnly]
            public NativeArray<int> m_TaxRates;

            public HealthcareParameterData m_HealthcareParameters;

            public ParkParameterData m_ParkParameters;

            public EducationParameterData m_EducationParameters;

            public TelecomParameterData m_TelecomParameters;

            public GarbageParameterData m_GarbageParameters;

            public PoliceConfigurationData m_PoliceParameters;

            public CitizenHappinessParameterData m_CitizenHappinessParameterData;

            public float m_BaseConsumptionSum;

            public uint m_SimulationFrame;

            public EntityCommandBuffer m_CommandBuffer;

            [ReadOnly]
            public Entity m_City;

            public NativeQueue<SetupQueueItem>.ParallelWriter m_PathfindQueue;

            public NativeQueue<RentAction>.ParallelWriter m_RentActionQueue;

            public EconomyParameterData m_EconomyParameters;

            public DemandParameterData m_DemandParameters;

            public NativeQueue<TriggerAction>.ParallelWriter m_TriggerBuffer;

            public NativeQueue<StatisticsEvent> m_StatisticsQueue;

            [ReadOnly]
            public RandomSeed m_RandomSeed;

            private void StartHomeFinding(Entity household, Entity commuteCitizen, Entity targetLocation, Entity oldHome, float minimumScore, bool targetIsOrigin, DynamicBuffer<HouseholdCitizen> citizens)
            {
                m_CommandBuffer.AddComponent(household, new PathInformation
                {
                    m_State = PathFlags.Pending
                });
                Household household2 = m_Households[household];
                PathfindWeights weights = default(PathfindWeights);
                if (m_Citizens.TryGetComponent(commuteCitizen, out var componentData))
                {
                    weights = CitizenUtils.GetPathfindWeights(componentData, household2, citizens.Length);
                }
                else
                {
                    for (int i = 0; i < citizens.Length; i++)
                    {
                        weights.m_Value += CitizenUtils.GetPathfindWeights(componentData, household2, citizens.Length).m_Value;
                    }
                    weights.m_Value *= 1f / (float)citizens.Length;
                }
                PathfindParameters pathfindParameters = default(PathfindParameters);
                pathfindParameters.m_MaxSpeed = 111.111115f;
                pathfindParameters.m_WalkSpeed = 1.6666667f;
                pathfindParameters.m_Weights = weights;
                pathfindParameters.m_Methods = PathMethod.Pedestrian | PathMethod.PublicTransportDay;
                pathfindParameters.m_MaxCost = CitizenBehaviorSystem.kMaxPathfindCost;
                pathfindParameters.m_PathfindFlags = PathfindFlags.Simplified | PathfindFlags.IgnorePath;
                PathfindParameters parameters = pathfindParameters;
                SetupQueueTarget setupQueueTarget = default(SetupQueueTarget);
                setupQueueTarget.m_Type = SetupTargetType.CurrentLocation;
                setupQueueTarget.m_Methods = PathMethod.Pedestrian;
                setupQueueTarget.m_Entity = targetLocation;
                SetupQueueTarget a = setupQueueTarget;
                setupQueueTarget = default(SetupQueueTarget);
                setupQueueTarget.m_Type = SetupTargetType.FindHome;
                setupQueueTarget.m_Methods = PathMethod.Pedestrian;
                setupQueueTarget.m_Entity = household;
                setupQueueTarget.m_Entity2 = oldHome;
                setupQueueTarget.m_Value2 = minimumScore;
                SetupQueueTarget b = setupQueueTarget;
                if (m_OwnedVehicles.TryGetBuffer(household, out var bufferData) && bufferData.Length != 0)
                {
                    parameters.m_Methods |= (PathMethod)(targetIsOrigin ? 2 : 6);
                    parameters.m_ParkingSize = float.MinValue;
                    parameters.m_IgnoredRules |= RuleFlags.ForbidCombustionEngines | RuleFlags.ForbidHeavyTraffic | RuleFlags.ForbidSlowTraffic;
                    a.m_Methods |= PathMethod.Road;
                    a.m_RoadTypes |= RoadTypes.Car;
                    b.m_Methods |= PathMethod.Road;
                    b.m_RoadTypes |= RoadTypes.Car;
                }
                if (targetIsOrigin)
                {
                    parameters.m_MaxSpeed.y = 277.77777f;
                    parameters.m_Methods |= PathMethod.Taxi | PathMethod.PublicTransportNight;
                    parameters.m_SecondaryIgnoredRules = VehicleUtils.GetIgnoredPathfindRulesTaxiDefaults();
                }
                else
                {
                    CommonUtils.Swap(ref a, ref b);
                }
                parameters.m_MaxResultCount = 10;
                parameters.m_PathfindFlags |= (PathfindFlags)(targetIsOrigin ? 256 : 128);
                m_CommandBuffer.AddBuffer<PathInformations>(household).Add(new PathInformations
                {
                    m_State = PathFlags.Pending
                });
                SetupQueueItem value = new SetupQueueItem(household, parameters, a, b);
                m_PathfindQueue.Enqueue(value);
            }

            private Entity GetFirstWorkplaceOrSchool(DynamicBuffer<HouseholdCitizen> citizens, ref Entity citizen)
            {
                for (int i = 0; i < citizens.Length; i++)
                {
                    citizen = citizens[i].m_Citizen;
                    if (m_Workers.HasComponent(citizen))
                    {
                        return m_Workers[citizen].m_Workplace;
                    }
                    if (m_Students.HasComponent(citizen))
                    {
                        return m_Students[citizen].m_School;
                    }
                }
                return Entity.Null;
            }

            private Entity GetCurrentLocation(DynamicBuffer<HouseholdCitizen> citizens)
            {
                for (int i = 0; i < citizens.Length; i++)
                {
                    if (m_CurrentBuildings.TryGetComponent(citizens[i].m_Citizen, out var componentData))
                    {
                        return componentData.m_CurrentBuilding;
                    }
                    if (m_CurrentTransports.TryGetComponent(citizens[i].m_Citizen, out var componentData2))
                    {
                        return componentData2.m_CurrentTransport;
                    }
                }
                return Entity.Null;
            }

            private void MoveAway(Entity household, DynamicBuffer<HouseholdCitizen> citizens)
            {
                m_CommandBuffer.AddComponent(household, default(MovingAway));
                m_CommandBuffer.RemoveComponent<PropertySeeker>(household);
                m_CommandBuffer.RemoveComponent<HomelessHousehold>(household);
            }

            public void Execute()
            {
                var random = m_RandomSeed.GetRandom(0);
                for (int i = 0; i < math.min(kMaxProcessEntitiesPerUpdate, m_Entities.Length); i++)
                {
                    Entity entity = m_Entities[random.NextInt(m_Entities.Length)];
                    DynamicBuffer<HouseholdCitizen> householdCitizenBuffer = m_CitizenBuffers[entity];
                    if (householdCitizenBuffer.Length == 0)
                    {
                        continue;
                    }
                    int householdIncome = EconomyUtils.GetHouseholdIncome(householdCitizenBuffer, ref m_Workers, ref m_Citizens, ref m_HealthProblems, ref m_EconomyParameters, m_TaxRates);
                    PropertySeeker propertySeeker = m_PropertySeekers[entity];
                    if (m_PathInformationBuffers.TryGetBuffer(entity, out var bufferData))
                    {
                        int num = 0;
                        PathInformations pathInformations = bufferData[num];
                        if ((pathInformations.m_State & PathFlags.Pending) != 0)
                        {
                            continue;
                        }
                        m_CommandBuffer.RemoveComponent<PathInformations>(entity);
                        bool flag = propertySeeker.m_TargetProperty != Entity.Null;
                        Entity entity2 = (flag ? pathInformations.m_Origin : pathInformations.m_Destination);
                        bool flag2 = false;
                        while (!m_CachedPropertyInfo.ContainsKey(entity2) || m_CachedPropertyInfo[entity2].free <= 0)
                        {
                            num++;
                            if (bufferData.Length > num)
                            {
                                pathInformations = bufferData[num];
                                entity2 = (flag ? pathInformations.m_Origin : pathInformations.m_Destination);
                                continue;
                            }
                            entity2 = Entity.Null;
                            flag2 = true;
                            break;
                        }
                        if (flag2 && bufferData.Length != 0 && bufferData[0].m_Destination != Entity.Null)
                        {
                            continue;
                        }
                        float num2 = float.NegativeInfinity;
                        if (entity2 != Entity.Null && m_CachedPropertyInfo.ContainsKey(entity2) && m_CachedPropertyInfo[entity2].free > 0)
                        {
                            num2 = PropertyUtils.GetPropertyScore(entity2, entity, householdCitizenBuffer, ref m_PrefabRefs, ref m_BuildingProperties, ref m_Buildings, ref m_BuildingDatas, ref m_Households, ref m_Citizens, ref m_Students, ref m_Workers, ref m_SpawnableDatas, ref m_Crimes, ref m_ServiceCoverages, ref m_Lockeds, ref m_ElectricityConsumers, ref m_WaterConsumers, ref m_GarbageProducers, ref m_MailProducers, ref m_Transforms, ref m_Abandoneds, ref m_Parks, ref m_Availabilities, m_TaxRates, m_PollutionMap, m_AirPollutionMap, m_NoiseMap, m_TelecomCoverages, m_CityModifiers[m_City], m_HealthcareParameters.m_HealthcareServicePrefab, m_ParkParameters.m_ParkServicePrefab, m_EducationParameters.m_EducationServicePrefab, m_TelecomParameters.m_TelecomServicePrefab, m_GarbageParameters.m_GarbageServicePrefab, m_PoliceParameters.m_PoliceServicePrefab, m_CitizenHappinessParameterData, m_GarbageParameters);
                        }
                        if (num2 < propertySeeker.m_BestPropertyScore)
                        {
                            entity2 = propertySeeker.m_BestProperty;
                        }
                        bool flag3 = (m_Households[entity].m_Flags & HouseholdFlags.MovedIn) != 0;
                        bool flag4 = entity2 != Entity.Null && BuildingUtils.IsHomelessShelterBuilding(entity2, ref m_Parks, ref m_Abandoneds);
                        bool flag5 = CitizenUtils.IsHouseholdNeedSupport(householdCitizenBuffer, ref m_Citizens, ref m_Students);
                        bool flag6 = m_PropertiesOnMarket.HasComponent(entity2) && (flag5 || m_PropertiesOnMarket[entity2].m_AskingRent < householdIncome);
                        bool flag7 = !m_PropertyRenters.HasComponent(entity) || !m_PropertyRenters[entity].m_Property.Equals(entity2);
                        bool hasValidHomelessHousehold = m_HomelessHouseholds.TryGetComponent(entity, out var homelessHousehold) && m_Buildings.HasComponent(homelessHousehold.m_TempHome);
                        if (m_PropertyRenters.HasComponent(entity) && m_PropertyRenters[entity].m_Property == entity2)
                        {
                            if (!flag5 && householdIncome < m_PropertyRenters[entity].m_Rent)
                            {
                                MoveAway(entity, householdCitizenBuffer);
                            }
                            else if (!flag4)
                            {
                                m_CommandBuffer.RemoveComponent<PropertySeeker>(entity);
                            }
                        }
                        else if ((flag6 && flag7) || (flag3 && flag4))
                        {
                            if (!(flag3 && flag4 && hasValidHomelessHousehold))
                            {
                                m_RentActionQueue.Enqueue(new RentAction
                                {
                                    m_Property = entity2,
                                    m_Renter = entity
                                });
                                if (m_CachedPropertyInfo.ContainsKey(entity2))
                                {
                                    CachedPropertyInformation value2 = m_CachedPropertyInfo[entity2];
                                    value2.free--;
                                    m_CachedPropertyInfo[entity2] = value2;
                                }
                            }
                            m_CommandBuffer.RemoveComponent<PropertySeeker>(entity);
                        }
                        else if (entity2 == Entity.Null && !hasValidHomelessHousehold)
                        {
                            MoveAway(entity, householdCitizenBuffer);
                        }
                        else
                        {
                            propertySeeker.m_BestProperty = default(Entity);
                            propertySeeker.m_BestPropertyScore = float.NegativeInfinity;
                            m_PropertySeekers[entity] = propertySeeker;
                        }
                    }
                    else
                    {
                        Entity propertyFromPropertyRenter = (m_PropertyRenters.HasComponent(entity) ? m_PropertyRenters[entity].m_Property : Entity.Null);
                        float bestPropertyScore = ((propertyFromPropertyRenter != Entity.Null) ? PropertyUtils.GetPropertyScore(propertyFromPropertyRenter, entity, householdCitizenBuffer, ref m_PrefabRefs, ref m_BuildingProperties, ref m_Buildings, ref m_BuildingDatas, ref m_Households, ref m_Citizens, ref m_Students, ref m_Workers, ref m_SpawnableDatas, ref m_Crimes, ref m_ServiceCoverages, ref m_Lockeds, ref m_ElectricityConsumers, ref m_WaterConsumers, ref m_GarbageProducers, ref m_MailProducers, ref m_Transforms, ref m_Abandoneds, ref m_Parks, ref m_Availabilities, m_TaxRates, m_PollutionMap, m_AirPollutionMap, m_NoiseMap, m_TelecomCoverages, m_CityModifiers[m_City], m_HealthcareParameters.m_HealthcareServicePrefab, m_ParkParameters.m_ParkServicePrefab, m_EducationParameters.m_EducationServicePrefab, m_TelecomParameters.m_TelecomServicePrefab, m_GarbageParameters.m_GarbageServicePrefab, m_PoliceParameters.m_PoliceServicePrefab, m_CitizenHappinessParameterData, m_GarbageParameters) : float.NegativeInfinity);
                        Entity citizen = Entity.Null;
                        Entity firstWorkplaceOrSchool = GetFirstWorkplaceOrSchool(householdCitizenBuffer, ref citizen);
                        bool noWorkplaceOrSchool = firstWorkplaceOrSchool == Entity.Null;
                        Entity origin = (noWorkplaceOrSchool ? GetCurrentLocation(householdCitizenBuffer) : firstWorkplaceOrSchool);
                        if (origin == Entity.Null)
                        {
                            UnityEngine.Debug.LogWarning($"No valid origin location to start home path finding for household:{entity.Index}, move away");
                            MoveAway(entity, householdCitizenBuffer);
                            continue;
                        }
                        propertySeeker.m_TargetProperty = firstWorkplaceOrSchool;
                        propertySeeker.m_BestProperty = propertyFromPropertyRenter;
                        propertySeeker.m_BestPropertyScore = bestPropertyScore;
                        m_PropertySeekers[entity] = propertySeeker;
                        StartHomeFinding(entity, citizen, origin, propertyFromPropertyRenter, propertySeeker.m_BestPropertyScore, noWorkplaceOrSchool, householdCitizenBuffer);
                    }
                }
            }
        }

        private struct TypeHandle
        {
            [ReadOnly]
            public EntityTypeHandle __Unity_Entities_Entity_TypeHandle;

            public ComponentLookup<BuildingPropertyData> __Game_Prefabs_BuildingPropertyData_RW_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<PrefabRef> __Game_Prefabs_PrefabRef_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<BuildingData> __Game_Prefabs_BuildingData_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<ParkData> __Game_Prefabs_ParkData_RO_ComponentLookup;

            [ReadOnly]
            public BufferLookup<Renter> __Game_Buildings_Renter_RO_BufferLookup;

            [ReadOnly]
            public ComponentLookup<Household> __Game_Citizens_Household_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<Abandoned> __Game_Buildings_Abandoned_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<Game.Buildings.Park> __Game_Buildings_Park_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<SpawnableBuildingData> __Game_Prefabs_SpawnableBuildingData_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<BuildingPropertyData> __Game_Prefabs_BuildingPropertyData_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<Building> __Game_Buildings_Building_RO_ComponentLookup;

            [ReadOnly]
            public BufferLookup<Game.Net.ServiceCoverage> __Game_Net_ServiceCoverage_RO_BufferLookup;

            [ReadOnly]
            public ComponentLookup<CrimeProducer> __Game_Buildings_CrimeProducer_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<Locked> __Game_Prefabs_Locked_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<Game.Objects.Transform> __Game_Objects_Transform_RO_ComponentLookup;

            [ReadOnly]
            public BufferLookup<CityModifier> __Game_City_CityModifier_RO_BufferLookup;

            [ReadOnly]
            public ComponentLookup<ElectricityConsumer> __Game_Buildings_ElectricityConsumer_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<WaterConsumer> __Game_Buildings_WaterConsumer_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<GarbageProducer> __Game_Buildings_GarbageProducer_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<MailProducer> __Game_Buildings_MailProducer_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<PropertyOnMarket> __Game_Buildings_PropertyOnMarket_RO_ComponentLookup;

            [ReadOnly]
            public BufferLookup<ResourceAvailability> __Game_Net_ResourceAvailability_RO_BufferLookup;

            [ReadOnly]
            public BufferLookup<PathInformations> __Game_Pathfind_PathInformations_RO_BufferLookup;

            [ReadOnly]
            public ComponentLookup<Worker> __Game_Citizens_Worker_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<Game.Citizens.Student> __Game_Citizens_Student_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<PropertyRenter> __Game_Buildings_PropertyRenter_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<HomelessHousehold> __Game_Citizens_HomelessHousehold_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<Citizen> __Game_Citizens_Citizen_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<HealthProblem> __Game_Citizens_HealthProblem_RO_ComponentLookup;

            [ReadOnly]
            public BufferLookup<OwnedVehicle> __Game_Vehicles_OwnedVehicle_RO_BufferLookup;

            [ReadOnly]
            public ComponentLookup<CurrentBuilding> __Game_Citizens_CurrentBuilding_RO_ComponentLookup;

            [ReadOnly]
            public ComponentLookup<CurrentTransport> __Game_Citizens_CurrentTransport_RO_ComponentLookup;

            [ReadOnly]
            public BufferLookup<HouseholdCitizen> __Game_Citizens_HouseholdCitizen_RO_BufferLookup;

            public ComponentLookup<PropertySeeker> __Game_Agents_PropertySeeker_RW_ComponentLookup;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void __AssignHandles(ref SystemState state)
            {
                __Unity_Entities_Entity_TypeHandle = state.GetEntityTypeHandle();
                __Game_Prefabs_BuildingPropertyData_RW_ComponentLookup = state.GetComponentLookup<BuildingPropertyData>();
                __Game_Prefabs_PrefabRef_RO_ComponentLookup = state.GetComponentLookup<PrefabRef>(isReadOnly: true);
                __Game_Prefabs_BuildingData_RO_ComponentLookup = state.GetComponentLookup<BuildingData>(isReadOnly: true);
                __Game_Prefabs_ParkData_RO_ComponentLookup = state.GetComponentLookup<ParkData>(isReadOnly: true);
                __Game_Buildings_Renter_RO_BufferLookup = state.GetBufferLookup<Renter>(isReadOnly: true);
                __Game_Citizens_Household_RO_ComponentLookup = state.GetComponentLookup<Household>(isReadOnly: true);
                __Game_Buildings_Abandoned_RO_ComponentLookup = state.GetComponentLookup<Abandoned>(isReadOnly: true);
                __Game_Buildings_Park_RO_ComponentLookup = state.GetComponentLookup<Game.Buildings.Park>(isReadOnly: true);
                __Game_Prefabs_SpawnableBuildingData_RO_ComponentLookup = state.GetComponentLookup<SpawnableBuildingData>(isReadOnly: true);
                __Game_Prefabs_BuildingPropertyData_RO_ComponentLookup = state.GetComponentLookup<BuildingPropertyData>(isReadOnly: true);
                __Game_Buildings_Building_RO_ComponentLookup = state.GetComponentLookup<Building>(isReadOnly: true);
                __Game_Net_ServiceCoverage_RO_BufferLookup = state.GetBufferLookup<Game.Net.ServiceCoverage>(isReadOnly: true);
                __Game_Buildings_CrimeProducer_RO_ComponentLookup = state.GetComponentLookup<CrimeProducer>(isReadOnly: true);
                __Game_Prefabs_Locked_RO_ComponentLookup = state.GetComponentLookup<Locked>(isReadOnly: true);
                __Game_Objects_Transform_RO_ComponentLookup = state.GetComponentLookup<Game.Objects.Transform>(isReadOnly: true);
                __Game_City_CityModifier_RO_BufferLookup = state.GetBufferLookup<CityModifier>(isReadOnly: true);
                __Game_Buildings_ElectricityConsumer_RO_ComponentLookup = state.GetComponentLookup<ElectricityConsumer>(isReadOnly: true);
                __Game_Buildings_WaterConsumer_RO_ComponentLookup = state.GetComponentLookup<WaterConsumer>(isReadOnly: true);
                __Game_Buildings_GarbageProducer_RO_ComponentLookup = state.GetComponentLookup<GarbageProducer>(isReadOnly: true);
                __Game_Buildings_MailProducer_RO_ComponentLookup = state.GetComponentLookup<MailProducer>(isReadOnly: true);
                __Game_Buildings_PropertyOnMarket_RO_ComponentLookup = state.GetComponentLookup<PropertyOnMarket>(isReadOnly: true);
                __Game_Net_ResourceAvailability_RO_BufferLookup = state.GetBufferLookup<ResourceAvailability>(isReadOnly: true);
                __Game_Pathfind_PathInformations_RO_BufferLookup = state.GetBufferLookup<PathInformations>(isReadOnly: true);
                __Game_Citizens_Worker_RO_ComponentLookup = state.GetComponentLookup<Worker>(isReadOnly: true);
                __Game_Citizens_Student_RO_ComponentLookup = state.GetComponentLookup<Game.Citizens.Student>(isReadOnly: true);
                __Game_Buildings_PropertyRenter_RO_ComponentLookup = state.GetComponentLookup<PropertyRenter>(isReadOnly: true);
                __Game_Citizens_HomelessHousehold_RO_ComponentLookup = state.GetComponentLookup<HomelessHousehold>(isReadOnly: true);
                __Game_Citizens_Citizen_RO_ComponentLookup = state.GetComponentLookup<Citizen>(isReadOnly: true);
                __Game_Citizens_HealthProblem_RO_ComponentLookup = state.GetComponentLookup<HealthProblem>(isReadOnly: true);
                __Game_Vehicles_OwnedVehicle_RO_BufferLookup = state.GetBufferLookup<OwnedVehicle>(isReadOnly: true);
                __Game_Citizens_CurrentBuilding_RO_ComponentLookup = state.GetComponentLookup<CurrentBuilding>(isReadOnly: true);
                __Game_Citizens_CurrentTransport_RO_ComponentLookup = state.GetComponentLookup<CurrentTransport>(isReadOnly: true);
                __Game_Citizens_HouseholdCitizen_RO_BufferLookup = state.GetBufferLookup<HouseholdCitizen>(isReadOnly: true);
                __Game_Agents_PropertySeeker_RW_ComponentLookup = state.GetComponentLookup<PropertySeeker>();
            }
        }

        public bool debugDisableHomeless;

        private const int UPDATE_INTERVAL = 16;

        public static readonly int kMaxProcessEntitiesPerUpdate = 128;

        [DebugWatchValue]
        private DebugWatchDistribution m_DefaultDistribution;

        [DebugWatchValue]
        private DebugWatchDistribution m_EvaluateDistributionLow;

        [DebugWatchValue]
        private DebugWatchDistribution m_EvaluateDistributionMedium;

        [DebugWatchValue]
        private DebugWatchDistribution m_EvaluateDistributionHigh;

        [DebugWatchValue]
        private DebugWatchDistribution m_EvaluateDistributionLowrent;

        private EntityQuery m_HouseholdQuery;

        private EntityQuery m_FreePropertyQuery;

        private EntityQuery m_EconomyParameterQuery;

        private EntityQuery m_DemandParameterQuery;

        private EndFrameBarrier m_EndFrameBarrier;

        private PathfindSetupSystem m_PathfindSetupSystem;

        private ResourceSystem m_ResourceSystem;

        private TaxSystem m_TaxSystem;

        private TriggerSystem m_TriggerSystem;

        private GroundPollutionSystem m_GroundPollutionSystem;

        private AirPollutionSystem m_AirPollutionSystem;

        private NoisePollutionSystem m_NoisePollutionSystem;

        private TelecomCoverageSystem m_TelecomCoverageSystem;

        private CitySystem m_CitySystem;

        private CityStatisticsSystem m_CityStatisticsSystem;

        private SimulationSystem m_SimulationSystem;

        private PropertyProcessingSystem m_PropertyProcessingSystem;

        private EntityQuery m_HealthcareParameterQuery;

        private EntityQuery m_ParkParameterQuery;

        private EntityQuery m_EducationParameterQuery;

        private EntityQuery m_TelecomParameterQuery;

        private EntityQuery m_GarbageParameterQuery;

        private EntityQuery m_PoliceParameterQuery;

        private EntityQuery m_CitizenHappinessParameterQuery;

        private TypeHandle __TypeHandle;

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 16;
        }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_EndFrameBarrier = base.World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_PathfindSetupSystem = base.World.GetOrCreateSystemManaged<PathfindSetupSystem>();
            m_ResourceSystem = base.World.GetOrCreateSystemManaged<ResourceSystem>();
            m_GroundPollutionSystem = base.World.GetOrCreateSystemManaged<GroundPollutionSystem>();
            m_AirPollutionSystem = base.World.GetOrCreateSystemManaged<AirPollutionSystem>();
            m_NoisePollutionSystem = base.World.GetOrCreateSystemManaged<NoisePollutionSystem>();
            m_TelecomCoverageSystem = base.World.GetOrCreateSystemManaged<TelecomCoverageSystem>();
            m_CitySystem = base.World.GetOrCreateSystemManaged<CitySystem>();
            m_CityStatisticsSystem = base.World.GetOrCreateSystemManaged<CityStatisticsSystem>();
            m_TaxSystem = base.World.GetOrCreateSystemManaged<TaxSystem>();
            m_TriggerSystem = base.World.GetOrCreateSystemManaged<TriggerSystem>();
            m_SimulationSystem = base.World.GetOrCreateSystemManaged<SimulationSystem>();
            m_PropertyProcessingSystem = base.World.GetOrCreateSystemManaged<PropertyProcessingSystem>();
            m_HouseholdQuery = GetEntityQuery(ComponentType.ReadWrite<Household>(), ComponentType.ReadWrite<PropertySeeker>(), ComponentType.ReadOnly<HouseholdCitizen>(), ComponentType.Exclude<MovingAway>(), ComponentType.Exclude<TouristHousehold>(), ComponentType.Exclude<CommuterHousehold>(), ComponentType.Exclude<CurrentBuilding>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            m_EconomyParameterQuery = GetEntityQuery(ComponentType.ReadOnly<EconomyParameterData>());
            m_DemandParameterQuery = GetEntityQuery(ComponentType.ReadOnly<DemandParameterData>());
            m_HealthcareParameterQuery = GetEntityQuery(ComponentType.ReadOnly<HealthcareParameterData>());
            m_ParkParameterQuery = GetEntityQuery(ComponentType.ReadOnly<ParkParameterData>());
            m_EducationParameterQuery = GetEntityQuery(ComponentType.ReadOnly<EducationParameterData>());
            m_TelecomParameterQuery = GetEntityQuery(ComponentType.ReadOnly<TelecomParameterData>());
            m_GarbageParameterQuery = GetEntityQuery(ComponentType.ReadOnly<GarbageParameterData>());
            m_PoliceParameterQuery = GetEntityQuery(ComponentType.ReadOnly<PoliceConfigurationData>());
            m_CitizenHappinessParameterQuery = GetEntityQuery(ComponentType.ReadOnly<CitizenHappinessParameterData>());
            EntityQueryDesc entityQueryDesc = new EntityQueryDesc
            {
                All = new ComponentType[1] { ComponentType.ReadOnly<Building>() },
                Any = new ComponentType[2]
                {
                ComponentType.ReadOnly<Abandoned>(),
                ComponentType.ReadOnly<Game.Buildings.Park>()
                },
                None = new ComponentType[3]
                {
                ComponentType.ReadOnly<Deleted>(),
                ComponentType.ReadOnly<Destroyed>(),
                ComponentType.ReadOnly<Temp>()
                }
            };
            EntityQueryDesc entityQueryDesc2 = new EntityQueryDesc
            {
                All = new ComponentType[3]
                {
                ComponentType.ReadOnly<PropertyOnMarket>(),
                ComponentType.ReadOnly<ResidentialProperty>(),
                ComponentType.ReadOnly<Building>()
                },
                None = new ComponentType[5]
                {
                ComponentType.ReadOnly<Abandoned>(),
                ComponentType.ReadOnly<Deleted>(),
                ComponentType.ReadOnly<Destroyed>(),
                ComponentType.ReadOnly<Temp>(),
                ComponentType.ReadOnly<Condemned>()
                }
            };
            m_FreePropertyQuery = GetEntityQuery(entityQueryDesc, entityQueryDesc2);
            RequireForUpdate(m_EconomyParameterQuery);
            RequireForUpdate(m_HealthcareParameterQuery);
            RequireForUpdate(m_ParkParameterQuery);
            RequireForUpdate(m_EducationParameterQuery);
            RequireForUpdate(m_TelecomParameterQuery);
            RequireForUpdate(m_HouseholdQuery);
            RequireForUpdate(m_DemandParameterQuery);
            m_DefaultDistribution = new DebugWatchDistribution(persistent: true, relative: true);
            m_EvaluateDistributionLow = new DebugWatchDistribution(persistent: true, relative: true);
            m_EvaluateDistributionMedium = new DebugWatchDistribution(persistent: true, relative: true);
            m_EvaluateDistributionHigh = new DebugWatchDistribution(persistent: true, relative: true);
            m_EvaluateDistributionLowrent = new DebugWatchDistribution(persistent: true, relative: true);
        }

        [Preserve]
        protected override void OnDestroy()
        {
            m_DefaultDistribution.Dispose();
            m_EvaluateDistributionLow.Dispose();
            m_EvaluateDistributionMedium.Dispose();
            m_EvaluateDistributionHigh.Dispose();
            m_EvaluateDistributionLowrent.Dispose();
            base.OnDestroy();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            NativeParallelHashMap<Entity, CachedPropertyInformation> cachedPropertyInfo = new NativeParallelHashMap<Entity, CachedPropertyInformation>(m_FreePropertyQuery.CalculateEntityCount(), Allocator.TempJob);
            JobHandle dependencies;
            NativeArray<GroundPollution> map = m_GroundPollutionSystem.GetMap(readOnly: true, out dependencies);
            JobHandle dependencies2;
            NativeArray<AirPollution> map2 = m_AirPollutionSystem.GetMap(readOnly: true, out dependencies2);
            JobHandle dependencies3;
            NativeArray<NoisePollution> map3 = m_NoisePollutionSystem.GetMap(readOnly: true, out dependencies3);
            JobHandle dependencies4;
            CellMapData<TelecomCoverage> data = m_TelecomCoverageSystem.GetData(readOnly: true, out dependencies4);
            __TypeHandle.__Game_Buildings_MailProducer_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Buildings_GarbageProducer_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Buildings_WaterConsumer_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Buildings_ElectricityConsumer_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_City_CityModifier_RO_BufferLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Objects_Transform_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Prefabs_Locked_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Buildings_CrimeProducer_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Net_ServiceCoverage_RO_BufferLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Buildings_Building_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Prefabs_BuildingPropertyData_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Prefabs_SpawnableBuildingData_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Buildings_Park_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Buildings_Abandoned_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Citizens_Household_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Buildings_Renter_RO_BufferLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Prefabs_ParkData_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Prefabs_BuildingData_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Prefabs_PrefabRef_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Prefabs_BuildingPropertyData_RW_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Unity_Entities_Entity_TypeHandle.Update(ref base.CheckedStateRef);
            PreparePropertyJob preparePropertyJob = default(PreparePropertyJob);
            preparePropertyJob.m_EntityType = __TypeHandle.__Unity_Entities_Entity_TypeHandle;
            preparePropertyJob.m_BuildingProperties = __TypeHandle.__Game_Prefabs_BuildingPropertyData_RW_ComponentLookup;
            preparePropertyJob.m_Prefabs = __TypeHandle.__Game_Prefabs_PrefabRef_RO_ComponentLookup;
            preparePropertyJob.m_BuildingDatas = __TypeHandle.__Game_Prefabs_BuildingData_RO_ComponentLookup;
            preparePropertyJob.m_ParkDatas = __TypeHandle.__Game_Prefabs_ParkData_RO_ComponentLookup;
            preparePropertyJob.m_Renters = __TypeHandle.__Game_Buildings_Renter_RO_BufferLookup;
            preparePropertyJob.m_Households = __TypeHandle.__Game_Citizens_Household_RO_ComponentLookup;
            preparePropertyJob.m_Abandoneds = __TypeHandle.__Game_Buildings_Abandoned_RO_ComponentLookup;
            preparePropertyJob.m_Parks = __TypeHandle.__Game_Buildings_Park_RO_ComponentLookup;
            preparePropertyJob.m_SpawnableDatas = __TypeHandle.__Game_Prefabs_SpawnableBuildingData_RO_ComponentLookup;
            preparePropertyJob.m_BuildingPropertyData = __TypeHandle.__Game_Prefabs_BuildingPropertyData_RO_ComponentLookup;
            preparePropertyJob.m_Buildings = __TypeHandle.__Game_Buildings_Building_RO_ComponentLookup;
            preparePropertyJob.m_ServiceCoverages = __TypeHandle.__Game_Net_ServiceCoverage_RO_BufferLookup;
            preparePropertyJob.m_Crimes = __TypeHandle.__Game_Buildings_CrimeProducer_RO_ComponentLookup;
            preparePropertyJob.m_Locked = __TypeHandle.__Game_Prefabs_Locked_RO_ComponentLookup;
            preparePropertyJob.m_Transforms = __TypeHandle.__Game_Objects_Transform_RO_ComponentLookup;
            preparePropertyJob.m_CityModifiers = __TypeHandle.__Game_City_CityModifier_RO_BufferLookup;
            preparePropertyJob.m_ElectricityConsumers = __TypeHandle.__Game_Buildings_ElectricityConsumer_RO_ComponentLookup;
            preparePropertyJob.m_WaterConsumers = __TypeHandle.__Game_Buildings_WaterConsumer_RO_ComponentLookup;
            preparePropertyJob.m_GarbageProducers = __TypeHandle.__Game_Buildings_GarbageProducer_RO_ComponentLookup;
            preparePropertyJob.m_MailProducers = __TypeHandle.__Game_Buildings_MailProducer_RO_ComponentLookup;
            preparePropertyJob.m_PollutionMap = map;
            preparePropertyJob.m_AirPollutionMap = map2;
            preparePropertyJob.m_NoiseMap = map3;
            preparePropertyJob.m_TelecomCoverages = data;
            preparePropertyJob.m_HealthcareParameters = m_HealthcareParameterQuery.GetSingleton<HealthcareParameterData>();
            preparePropertyJob.m_ParkParameters = m_ParkParameterQuery.GetSingleton<ParkParameterData>();
            preparePropertyJob.m_EducationParameters = m_EducationParameterQuery.GetSingleton<EducationParameterData>();
            preparePropertyJob.m_TelecomParameters = m_TelecomParameterQuery.GetSingleton<TelecomParameterData>();
            preparePropertyJob.m_GarbageParameters = m_GarbageParameterQuery.GetSingleton<GarbageParameterData>();
            preparePropertyJob.m_PoliceParameters = m_PoliceParameterQuery.GetSingleton<PoliceConfigurationData>();
            preparePropertyJob.m_CitizenHappinessParameterData = m_CitizenHappinessParameterQuery.GetSingleton<CitizenHappinessParameterData>();
            preparePropertyJob.m_City = m_CitySystem.City;
            preparePropertyJob.m_PropertyData = cachedPropertyInfo.AsParallelWriter();
            PreparePropertyJob jobData = preparePropertyJob;
            __TypeHandle.__Game_Agents_PropertySeeker_RW_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Citizens_HouseholdCitizen_RO_BufferLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Citizens_CurrentTransport_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Citizens_CurrentBuilding_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Citizens_Household_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Buildings_MailProducer_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Buildings_GarbageProducer_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Buildings_WaterConsumer_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Buildings_ElectricityConsumer_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Vehicles_OwnedVehicle_RO_BufferLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Buildings_Park_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Buildings_Abandoned_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Citizens_HealthProblem_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_City_CityModifier_RO_BufferLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Objects_Transform_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Prefabs_Locked_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Buildings_CrimeProducer_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Citizens_Citizen_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Citizens_HomelessHousehold_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Buildings_PropertyRenter_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Citizens_Student_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Citizens_Worker_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Net_ServiceCoverage_RO_BufferLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Prefabs_PrefabRef_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Pathfind_PathInformations_RO_BufferLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Buildings_Building_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Prefabs_BuildingPropertyData_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Prefabs_SpawnableBuildingData_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Net_ResourceAvailability_RO_BufferLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Buildings_PropertyOnMarket_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            __TypeHandle.__Game_Prefabs_BuildingData_RO_ComponentLookup.Update(ref base.CheckedStateRef);
            JobHandle outJobHandle;
            JobHandle deps;
            JobHandle deps2;
            FindPropertyJob jobData2 = new FindPropertyJob
            {
                m_Entities = m_HouseholdQuery.ToEntityListAsync(base.World.UpdateAllocator.ToAllocator, out outJobHandle),
                m_CachedPropertyInfo = cachedPropertyInfo,
                m_BuildingDatas = __TypeHandle.__Game_Prefabs_BuildingData_RO_ComponentLookup,
                m_PropertiesOnMarket = __TypeHandle.__Game_Buildings_PropertyOnMarket_RO_ComponentLookup,
                m_Availabilities = __TypeHandle.__Game_Net_ResourceAvailability_RO_BufferLookup,
                m_SpawnableDatas = __TypeHandle.__Game_Prefabs_SpawnableBuildingData_RO_ComponentLookup,
                m_BuildingProperties = __TypeHandle.__Game_Prefabs_BuildingPropertyData_RO_ComponentLookup,
                m_Buildings = __TypeHandle.__Game_Buildings_Building_RO_ComponentLookup,
                m_PathInformationBuffers = __TypeHandle.__Game_Pathfind_PathInformations_RO_BufferLookup,
                m_PrefabRefs = __TypeHandle.__Game_Prefabs_PrefabRef_RO_ComponentLookup,
                m_ServiceCoverages = __TypeHandle.__Game_Net_ServiceCoverage_RO_BufferLookup,
                m_Workers = __TypeHandle.__Game_Citizens_Worker_RO_ComponentLookup,
                m_Students = __TypeHandle.__Game_Citizens_Student_RO_ComponentLookup,
                m_PropertyRenters = __TypeHandle.__Game_Buildings_PropertyRenter_RO_ComponentLookup,
                m_HomelessHouseholds = __TypeHandle.__Game_Citizens_HomelessHousehold_RO_ComponentLookup,
                m_Citizens = __TypeHandle.__Game_Citizens_Citizen_RO_ComponentLookup,
                m_Crimes = __TypeHandle.__Game_Buildings_CrimeProducer_RO_ComponentLookup,
                m_Lockeds = __TypeHandle.__Game_Prefabs_Locked_RO_ComponentLookup,
                m_Transforms = __TypeHandle.__Game_Objects_Transform_RO_ComponentLookup,
                m_CityModifiers = __TypeHandle.__Game_City_CityModifier_RO_BufferLookup,
                m_HealthProblems = __TypeHandle.__Game_Citizens_HealthProblem_RO_ComponentLookup,
                m_Abandoneds = __TypeHandle.__Game_Buildings_Abandoned_RO_ComponentLookup,
                m_Parks = __TypeHandle.__Game_Buildings_Park_RO_ComponentLookup,
                m_OwnedVehicles = __TypeHandle.__Game_Vehicles_OwnedVehicle_RO_BufferLookup,
                m_ElectricityConsumers = __TypeHandle.__Game_Buildings_ElectricityConsumer_RO_ComponentLookup,
                m_WaterConsumers = __TypeHandle.__Game_Buildings_WaterConsumer_RO_ComponentLookup,
                m_GarbageProducers = __TypeHandle.__Game_Buildings_GarbageProducer_RO_ComponentLookup,
                m_MailProducers = __TypeHandle.__Game_Buildings_MailProducer_RO_ComponentLookup,
                m_Households = __TypeHandle.__Game_Citizens_Household_RO_ComponentLookup,
                m_CurrentBuildings = __TypeHandle.__Game_Citizens_CurrentBuilding_RO_ComponentLookup,
                m_CurrentTransports = __TypeHandle.__Game_Citizens_CurrentTransport_RO_ComponentLookup,
                m_CitizenBuffers = __TypeHandle.__Game_Citizens_HouseholdCitizen_RO_BufferLookup,
                m_PropertySeekers = __TypeHandle.__Game_Agents_PropertySeeker_RW_ComponentLookup,
                m_PollutionMap = map,
                m_AirPollutionMap = map2,
                m_NoiseMap = map3,
                m_TelecomCoverages = data,
                m_HealthcareParameters = m_HealthcareParameterQuery.GetSingleton<HealthcareParameterData>(),
                m_ParkParameters = m_ParkParameterQuery.GetSingleton<ParkParameterData>(),
                m_EducationParameters = m_EducationParameterQuery.GetSingleton<EducationParameterData>(),
                m_TelecomParameters = m_TelecomParameterQuery.GetSingleton<TelecomParameterData>(),
                m_GarbageParameters = m_GarbageParameterQuery.GetSingleton<GarbageParameterData>(),
                m_PoliceParameters = m_PoliceParameterQuery.GetSingleton<PoliceConfigurationData>(),
                m_CitizenHappinessParameterData = m_CitizenHappinessParameterQuery.GetSingleton<CitizenHappinessParameterData>(),
                m_ResourcePrefabs = m_ResourceSystem.GetPrefabs(),
                m_TaxRates = m_TaxSystem.GetTaxRates(),
                m_EconomyParameters = m_EconomyParameterQuery.GetSingleton<EconomyParameterData>(),
                m_DemandParameters = m_DemandParameterQuery.GetSingleton<DemandParameterData>(),
                m_BaseConsumptionSum = m_ResourceSystem.BaseConsumptionSum,
                m_SimulationFrame = m_SimulationSystem.frameIndex,
                m_RentActionQueue = m_PropertyProcessingSystem.GetRentActionQueue(out deps).AsParallelWriter(),
                m_City = m_CitySystem.City,
                m_PathfindQueue = m_PathfindSetupSystem.GetQueue(this, 80, 16).AsParallelWriter(),
                m_TriggerBuffer = m_TriggerSystem.CreateActionBuffer().AsParallelWriter(),
                m_CommandBuffer = m_EndFrameBarrier.CreateCommandBuffer(),
                m_StatisticsQueue = m_CityStatisticsSystem.GetStatisticsEventQueue(out deps2)
            };
            JobHandle job = JobChunkExtensions.ScheduleParallel(jobData, m_FreePropertyQuery, JobUtils.CombineDependencies(base.Dependency, dependencies, dependencies3, dependencies2, dependencies4, deps));
            base.Dependency = IJobExtensions.Schedule(jobData2, JobHandle.CombineDependencies(job, outJobHandle, deps2));
            m_EndFrameBarrier.AddJobHandleForProducer(base.Dependency);
            m_PathfindSetupSystem.AddQueueWriter(base.Dependency);
            m_ResourceSystem.AddPrefabsReader(base.Dependency);
            m_AirPollutionSystem.AddReader(base.Dependency);
            m_NoisePollutionSystem.AddReader(base.Dependency);
            m_GroundPollutionSystem.AddReader(base.Dependency);
            m_TelecomCoverageSystem.AddReader(base.Dependency);
            m_TriggerSystem.AddActionBufferWriter(base.Dependency);
            m_CityStatisticsSystem.AddWriter(base.Dependency);
            m_TaxSystem.AddReader(base.Dependency);
            cachedPropertyInfo.Dispose(base.Dependency);
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
        public ModifiedHouseholdFindPropertySystem()
        {
        }
    }
}