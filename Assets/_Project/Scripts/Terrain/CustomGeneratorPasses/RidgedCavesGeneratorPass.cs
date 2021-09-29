using System.Runtime.CompilerServices;
using Eldemarkki.VoxelTerrain.Settings;
using Eldemarkki.VoxelTerrain.Utilities;
using Eldemarkki.VoxelTerrain.VoxelData;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;


[CreateAssetMenu(fileName = "BillowCavesGeneratorPass", menuName = "Custom/Terrain/Billow Caves Generator Pass", order = -1)]
#region Asset
public class BillowCavesGeneratorPass : TerrainGeneratorPassAsset {

    [Header("Parameters")]
    public BillowCavesGeneratorPass_Parameters parameters;

    public override JobHandleWithData<IVoxelDataGenerationJob> GetGeneratorPassHandle (VoxelDataVolume voxelData, Bounds bounds, int seed, float size) {
        BillowCavesGeneratorPass_CalculationJob job = new BillowCavesGeneratorPass_CalculationJob {
            param = parameters,
            worldPositionOffset = bounds.min.ToInt3(),
            outputVoxelData = voxelData,
            seed = seed,
            size = size
        };

        JobHandle jobHandle = job.Schedule(voxelData.Length, 256);
        JobHandleWithData<IVoxelDataGenerationJob> jobHandleWithData = new JobHandleWithData<IVoxelDataGenerationJob>();
        jobHandleWithData.JobHandle = jobHandle;
        jobHandleWithData.JobData = job;

        return jobHandleWithData;
    }
}
#endregion


[System.Serializable]
public struct BillowCavesGeneratorPass_Parameters {
    public float noiseFrequency;                //0.034
    public int noiseOctaveCount;                //4
    public float islandTop;                     //10
    public float islandBottom;                  //-30
    public float islandBottomMin;                  
    public float islandRadius;                  //96
    public float islandRadiusMin;                  
    public float softShapeOverlayMultiplier;    //0.2
    public float softShapeMaxClamp;             //0.7
    public float hillsMaxHeight;                //16
    public float minHillMul;                    //0.3
    public float maxHillMul;                    //0.6
}


[BurstCompile]
public struct BillowCavesGeneratorPass_CalculationJob : IVoxelDataGenerationJob {

    public BillowCavesGeneratorPass_Parameters param { get; set; }
    public VoxelDataVolume outputVoxelData { get; set; }
    public int3 worldPositionOffset { get; set; }
    public int seed { get; set; }
    public float size { get; set; }


    public void Execute (int index) {
        int3 worldPosition = IndexUtilities.IndexToXyz(index, outputVoxelData.Width, outputVoxelData.Height) + worldPositionOffset;

        float voxelData = CalculateVoxelData(worldPosition);
        outputVoxelData.SetVoxelData(voxelData, index, 0);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float CalculateVoxelData (float3 worldPosition) {

        float radius = math.lerp(param.islandRadiusMin, param.islandRadius, size);
        float bottom = math.lerp(param.islandBottomMin, param.islandBottom, size);


        // Preparing and sampeling height-relative value
        float4 baseSoftNoise = GeneratorPassUtils.OctaveNoiseBillowStrange(worldPosition.x, worldPosition.y, worldPosition.z, param.noiseFrequency, param.noiseOctaveCount, seed);
        float bottomFadeValue = math.saturate(math.unlerp(bottom, bottom + 10f, worldPosition.y));
        float topToBottomValue = math.saturate(math.unlerp(bottom, param.islandTop, worldPosition.y));
        float topToUpperRegionValue = math.select(0.8f, math.lerp(param.maxHillMul, param.minHillMul, math.saturate(math.unlerp(param.islandTop, param.islandTop + param.hillsMaxHeight, worldPosition.y))), worldPosition.y > param.islandTop);
        float radiusAtHeight = math.lerp(0, topToBottomValue, radius);
        float inlerpUnder = math.saturate(math.unlerp(bottom - 8, param.islandTop, worldPosition.y));
        float radiusUnder = math.lerp(0, inlerpUnder, radius);

        float layersBase = GeneratorPassUtils.nfmod(worldPosition.y * 0.05f);
        float layersWave = math.smoothstep(0f, 1f, math.abs(2f * layersBase - 1));

        // The distance from center line to a point in the XZ plane
        float cylinderDistance = math.sqrt(worldPosition.x * worldPosition.x + worldPosition.z * worldPosition.z);


        // Composing a soft cone shape
        float softConeShape = cylinderDistance;
        softConeShape /= radiusAtHeight;
        softConeShape = 1f - softConeShape;
        softConeShape = math.clamp(softConeShape * bottomFadeValue, 0f, param.softShapeMaxClamp);
        softConeShape *= math.select(0f, 1f, worldPosition.y > bottom);
        softConeShape *= math.lerp(math.lerp(0.4f, 0.6f, noise.snoise(worldPosition * 0.05f) * 0.5f + 0.5f), 1f, layersWave);

        // Merging noise and shape
        float softShape = (softConeShape * topToUpperRegionValue);
        return (baseSoftNoise.x * softShape) + softShape * param.softShapeOverlayMultiplier;
    }
}