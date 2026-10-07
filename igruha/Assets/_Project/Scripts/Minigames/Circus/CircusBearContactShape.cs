using System;
using System.Collections.Generic;
using Igruha.Core.Player;
using UnityEngine;

namespace Igruha.Minigames.Circus
{
    /// <summary>
    /// A small, pose-following contact envelope of the victim's real torso.
    /// Uses the existing cached skin probes; no readable mesh, BakeMesh or player modification.
    /// This is visual clearance only. The authoritative bear still decides whether a strike hit.
    /// </summary>
    public sealed class CircusBearContactShape
    {
        public const float PawRadius=.18f;
        private const float SkinClearance=.035f;
        private const float UnmeasuredSkinAllowance=.055f;
        private const float ProbeSliceOverlap=.045f;
        private const int SectorCount=CharacterTorsoShape.SectorCount;
        private const int SweepSamples=12;
        private const int ProjectionPasses=3;

        private sealed class ArmPart
        {
            internal Transform Start,End;
            internal Vector3 A,B;
            internal float Radius;
        }
        private ArmPart[] arms;
        private int[] armByBone;
        private PlayerController player;
        private CapsuleCollider capsule;
        private Transform[] bones;
        private CharacterTorsoShape.Slice[] slices;
        private CharacterSkinProbes.Probe[] probes;
        private Matrix4x4[] bindPoses,skinMatrices;
        private int[] usedBones;
        private Vector3[] centers,axes,sides,fronts,meshAxes,meshSides,meshFronts;
        private float[] outerRadii,halfHeights;
        private float referenceMeshScale=1f;
        private int poseFrame=-1;

        public PlayerController BoundPlayer=>player;
        public bool HasSkinEnvelope=>slices!=null && probes!=null && probes.Length>0;
        public Vector3 BodyCenter
        {
            get
            {
                EnsurePose();
                if(player==null) return Vector3.zero;
                if(slices!=null) return centers[ContactSlice];
                return capsule!=null ? capsule.transform.TransformPoint(capsule.center) : player!=null ? player.Position+Vector3.up : Vector3.zero;
            }
        }
        private int ContactSlice=>Mathf.Clamp(Mathf.RoundToInt((slices.Length-1)*.6f),0,slices.Length-1);

        public void Bind(PlayerController value)
        {
            if(player==value && value!=null) return;
            player=value;capsule=null;arms=null;armByBone=null;bones=null;slices=null;probes=null;bindPoses=null;
            skinMatrices=null;usedBones=null;centers=null;axes=null;sides=null;fronts=null;outerRadii=null;halfHeights=null;
            meshAxes=null;meshSides=null;meshFronts=null;referenceMeshScale=1f;poseFrame=-1;
            if(player==null) return;
            capsule=player.GetComponent<CapsuleCollider>();
            var shapes=CharacterTorsoShape.Shared;
            var skins=CharacterSkinProbes.Shared;
            if(shapes==null) return;
            foreach(var renderer in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if(!shapes.TryGet(renderer.sharedMesh,out CharacterTorsoShape.Entry torso) || torso.Slices==null || torso.Slices.Length==0) continue;
                Transform[] candidateBones=renderer.bones;
                if(candidateBones.Length!=torso.BoneCount) continue;
                bool valid=true;
                foreach(var slice in torso.Slices)
                    if(slice.Bone<0 || slice.Bone>=candidateBones.Length || candidateBones[slice.Bone]==null) valid=false;
                if(!valid) continue;
                bones=candidateBones;slices=torso.Slices;
                centers=new Vector3[slices.Length];axes=new Vector3[slices.Length];
                sides=new Vector3[slices.Length];fronts=new Vector3[slices.Length];
                outerRadii=new float[slices.Length*SectorCount];halfHeights=new float[slices.Length];
                if(skins!=null && skins.TryGet(renderer.sharedMesh,out CharacterSkinProbes.Entry skin)
                    && skin.BindPoses!=null && skin.BindPoses.Length==bones.Length && skin.Probes!=null)
                {
                    var selected=new List<CharacterSkinProbes.Probe>();
                    var used=new bool[bones.Length];
                    foreach(var probe in skin.Probes)
                    {
                        if(probe.Group<2 || probe.Group>4) continue;
                        bool usable=true;
                        for(int slot=0;slot<CharacterSkinProbes.BonesPerProbe;slot++)
                            if(probe.Weights[slot]>0 && (probe.Bone(slot)>=bones.Length || bones[probe.Bone(slot)]==null)) usable=false;
                        if(!usable) continue;
                        selected.Add(probe);
                        for(int slot=0;slot<CharacterSkinProbes.BonesPerProbe;slot++)
                            if(probe.Weights[slot]>0) used[probe.Bone(slot)]=true;
                    }
                    var indices=new List<int>();for(int i=0;i<used.Length;i++) if(used[i]) indices.Add(i);
                    probes=selected.ToArray();usedBones=indices.ToArray();bindPoses=skin.BindPoses;
                    skinMatrices=new Matrix4x4[bones.Length];
                    CreateArms();
                    CacheReferenceScale();
                }
                break;
            }
        }

        private void CreateArms()
        {
            armByBone=new int[bones.Length];for(int i=0;i<armByBone.Length;i++)armByBone[i]=-1;
            var result=new List<ArmPart>();
            foreach(var probe in probes)
            {
                if(probe.Group==4)continue;
                int bone=DominantBone(probe);if(armByBone[bone]>=0)continue;
                Transform start=bones[bone],end=start;
                if(start.childCount>0)end=start.GetChild(0);
                // The palm ends at the middle knuckle. Each finger then gets its
                // own thin capsule; finger length must not inflate the whole palm.
                for(int child=0;child<start.childCount;child++)
                {
                    Transform candidate=start.GetChild(child);
                    if(candidate.name.IndexOf("Middle",StringComparison.OrdinalIgnoreCase)>=0){end=candidate;break;}
                }
                armByBone[bone]=result.Count;
                result.Add(new ArmPart{Start=start,End=end});
            }
            arms=result.ToArray();
        }

        private static int DominantBone(CharacterSkinProbes.Probe probe)
        {
            int slot=0;for(int i=1;i<CharacterSkinProbes.BonesPerProbe;i++)if(probe.Weights[i]>probe.Weights[slot])slot=i;
            return probe.Bone(slot);
        }

        private static Vector3 ClosestAxisPoint(ArmPart part,Vector3 point)
        {
            Vector3 axis=part.B-part.A;
            return part.A+axis*Mathf.Clamp01(Vector3.Dot(point-part.A,axis)/Mathf.Max(axis.sqrMagnitude,.000001f));
        }

        private void CacheReferenceScale()
        {
            // Shape radii were baked in world metres, while slice centres retain bone-local units.
            // Recover that authored metres-per-mesh-unit ratio from adjacent slice centres, so a
            // scaled instance or an animated nonuniform torso scale does not reuse unscaled radii.
            float shortest=float.PositiveInfinity;
            for(int i=0;i<slices.Length-1;i++)
            {
                Vector3 a=bindPoses[slices[i].Bone].inverse.MultiplyPoint3x4(slices[i].Center);
                Vector3 b=bindPoses[slices[i+1].Bone].inverse.MultiplyPoint3x4(slices[i+1].Center);
                float distance=Vector3.Distance(a,b);
                if(distance>.00001f && distance<shortest)
                { shortest=distance;referenceMeshScale=(slices[i].HalfHeight+slices[i+1].HalfHeight)/distance; }
            }
            referenceMeshScale=Mathf.Max(referenceMeshScale,.000001f);
            meshAxes=new Vector3[slices.Length];meshSides=new Vector3[slices.Length];meshFronts=new Vector3[slices.Length];
            for(int i=0;i<slices.Length;i++)
            {
                Matrix4x4 inverse=bindPoses[slices[i].Bone].inverse;
                meshAxes[i]=inverse.MultiplyVector(slices[i].Axis).normalized;
                meshSides[i]=inverse.MultiplyVector(slices[i].Side).normalized;
                meshFronts[i]=inverse.MultiplyVector(slices[i].Front).normalized;
            }
        }

        public void RefreshPose()
        {
            poseFrame=Time.frameCount;
            if(player==null || slices==null) return;
            for(int i=0;i<slices.Length;i++)
            {
                var slice=slices[i];Transform bone=bones[slice.Bone];
                centers[i]=bone.TransformPoint(slice.Center);
                axes[i]=bone.TransformDirection(slice.Axis).normalized;
                sides[i]=bone.TransformDirection(slice.Side).normalized;
                fronts[i]=bone.TransformDirection(slice.Front).normalized;
                float axisScale=player.transform.lossyScale.y;
                float sideScale=Mathf.Max(Mathf.Abs(player.transform.lossyScale.x),Mathf.Abs(player.transform.lossyScale.z));
                float frontScale=sideScale;
                if(meshAxes!=null)
                {
                    Matrix4x4 deformation=bone.localToWorldMatrix*bindPoses[slice.Bone];
                    Vector3 axis=deformation.MultiplyVector(meshAxes[i]);
                    Vector3 side=deformation.MultiplyVector(meshSides[i]);
                    Vector3 front=deformation.MultiplyVector(meshFronts[i]);
                    axisScale=axis.magnitude/referenceMeshScale;sideScale=side.magnitude/referenceMeshScale;frontScale=front.magnitude/referenceMeshScale;
                    axes[i]=axis.normalized;sides[i]=side.normalized;fronts[i]=front.normalized;
                }
                halfHeights[i]=slice.HalfHeight*Mathf.Abs(axisScale);
                for(int sector=0;sector<SectorCount;sector++)
                {
                    float angle=sector*Mathf.PI*2/SectorCount;
                    float scale=new Vector2(Mathf.Cos(angle)*sideScale,Mathf.Sin(angle)*frontScale).magnitude;
                    outerRadii[i*SectorCount+sector]=((slice.Radii!=null && slice.Radii.Length==SectorCount ? slice.Radii[sector] : .2f)+UnmeasuredSkinAllowance)*scale;
                }
            }
            Vector3 playerScale=player.transform.lossyScale;
            float minimumArmRadius=.015f*Mathf.Max(Mathf.Abs(playerScale.x),Mathf.Max(Mathf.Abs(playerScale.y),Mathf.Abs(playerScale.z)));
            if(arms!=null)
                foreach(var part in arms)
                {
                    part.A=part.Start.position;part.B=part.End.position;
                    part.Radius=minimumArmRadius;
                }
            if(probes==null) return;
            foreach(int bone in usedBones) skinMatrices[bone]=bones[bone].localToWorldMatrix*bindPoses[bone];
            foreach(var probe in probes)
            {
                Vector3 point=Vector3.zero;
                for(int slot=0;slot<CharacterSkinProbes.BonesPerProbe;slot++)
                    if(probe.Weights[slot]>0) point+=skinMatrices[probe.Bone(slot)].MultiplyPoint3x4(probe.Position)*probe.Weights[slot];
                if(probe.Group!=4)
                {
                    int index=armByBone[DominantBone(probe)];
                    if(index>=0)
                    {
                        ArmPart part=arms[index];
                        part.Radius=Mathf.Max(part.Radius,Vector3.Distance(point,ClosestAxisPoint(part,point)));
                    }
                    continue;
                }
                for(int i=0;i<slices.Length;i++)
                {
                    Vector3 offset=point-centers[i];float along=Vector3.Dot(offset,axes[i]);
                    if(Mathf.Abs(along)>halfHeights[i]+ProbeSliceOverlap) continue;
                    Vector3 radial=offset-axes[i]*along;
                    float angle=Mathf.Atan2(Vector3.Dot(radial,fronts[i]),Vector3.Dot(radial,sides[i]));
                    int sector=Mathf.RoundToInt(Mathf.Repeat(angle/(Mathf.PI*2),1)*SectorCount)%SectorCount;
                    int index=i*SectorCount+sector;
                    outerRadii[index]=Mathf.Max(outerRadii[index],radial.magnitude);
                }
            }
        }

        private void EnsurePose() { if(poseFrame!=Time.frameCount) RefreshPose(); }

        public Vector3 SurfaceToward(Vector3 approachPoint,float pawRadius=PawRadius)
        {
            EnsurePose();
            if(player==null) return approachPoint;
            if(slices==null)
            {
                CapsuleAxis(out Vector3 a,out Vector3 b,out float radius);
                Vector3 center=(a+b)*.5f;Vector3 outward=Vector3.ProjectOnPlane(approachPoint-center,b-a).normalized;
                if(outward.sqrMagnitude<.001f) outward=player.transform.forward;
                return center+outward*(radius+pawRadius+SkinClearance);
            }
            int s=ContactSlice;
            Vector3 radial=Vector3.ProjectOnPlane(approachPoint-centers[s],axes[s]).normalized;
            if(radial.sqrMagnitude<.001f) radial=fronts[s];
            return ProjectOutside(centers[s]+radial*(Radius(s,radial)+pawRadius+SkinClearance),approachPoint,pawRadius);
        }

        /// <summary>Clearance between the solved paw sphere and any part of the
        /// current torso envelope. A swipe can touch beside its aiming point.
        /// Zero means touching/overlapping; a missing victim has infinite clearance.</summary>
        public float SurfaceGap(Vector3 pawPoint,float pawRadius=PawRadius)
        {
            EnsurePose();
            if(player==null)return float.PositiveInfinity;
            if(slices==null)
            {
                CapsuleAxis(out Vector3 a,out Vector3 b,out float radius);
                Vector3 segment=b-a;
                Vector3 center=a+segment*Mathf.Clamp01(Vector3.Dot(pawPoint-a,segment)/Mathf.Max(segment.sqrMagnitude,.0001f));
                return Mathf.Max(0,Vector3.Distance(pawPoint,center)-radius-SkinClearance-pawRadius);
            }
            float nearest=float.PositiveInfinity;
            for(int i=0;i<slices.Length;i++)
            {
                Vector3 offset=pawPoint-centers[i];float along=Vector3.Dot(offset,axes[i]);
                Vector3 radial=offset-axes[i]*along;
                Vector3 direction=radial.sqrMagnitude>.000001f ? radial.normalized : fronts[i];
                float radialGap=Mathf.Max(0,radial.magnitude-Radius(i,direction)-SkinClearance);
                float capGap=Mathf.Max(0,Mathf.Abs(along)-halfHeights[i]);
                // The same rounded cap as ProjectOutside, including the paw's
                // radius. Distance to an arbitrary front-centre target is unrelated
                // to contact when the sweep slides along an adjacent torso slice.
                float gap=Mathf.Sqrt(radialGap*radialGap+capGap*capGap)-pawRadius;
                nearest=Mathf.Min(nearest,Mathf.Max(0,gap));
            }
            if(arms!=null)
                foreach(var part in arms)
                {
                    float gap=Vector3.Distance(pawPoint,ClosestAxisPoint(part,pawPoint))-part.Radius-SkinClearance-pawRadius;
                    nearest=Mathf.Min(nearest,Mathf.Max(0,gap));
                }
            return nearest;
        }

        public Vector3 ProjectOutside(Vector3 pawPoint,Vector3 approachPoint,float pawRadius=PawRadius)
        {
            EnsurePose();
            if(player==null) return pawPoint;
            if(slices==null)
            {
                CapsuleAxis(out Vector3 a,out Vector3 b,out float radius);
                Vector3 segment=b-a;
                Vector3 center=a+segment*Mathf.Clamp01(Vector3.Dot(pawPoint-a,segment)/Mathf.Max(segment.sqrMagnitude,.0001f));
                Vector3 radial=pawPoint-center;float wanted=radius+pawRadius+SkinClearance;
                if(radial.sqrMagnitude>=wanted*wanted) return pawPoint;
                Vector3 outward=radial.sqrMagnitude>.0001f ? radial.normalized : (approachPoint-center).normalized;
                if(outward.sqrMagnitude<.001f) outward=player.transform.forward;
                return center+outward*wanted;
            }
            for(int pass=0;pass<ProjectionPasses;pass++)
            {
                bool moved=false;
                for(int i=0;i<slices.Length;i++)
                {
                    Vector3 offset=pawPoint-centers[i];float along=Vector3.Dot(offset,axes[i]);
                    float beyond=Mathf.Max(0,Mathf.Abs(along)-halfHeights[i]);
                    if(beyond>=pawRadius) continue;
                    Vector3 radial=offset-axes[i]*along;
                    Vector3 toward=Vector3.ProjectOnPlane(approachPoint-centers[i],axes[i]).normalized;
                    Vector3 outward=radial.sqrMagnitude>.0001f ? radial.normalized : toward;
                    if(outward.sqrMagnitude<.001f) outward=fronts[i];
                    float wanted=Radius(i,outward)+SkinClearance+Mathf.Sqrt(Mathf.Max(0,pawRadius*pawRadius-beyond*beyond));
                    if(radial.sqrMagnitude>=wanted*wanted) continue;
                    // An undersampled fast swipe must not be expelled through the far side of the torso.
                    if(Vector3.Dot(outward,toward)<-.1f) outward=toward.sqrMagnitude>.001f ? toward : fronts[i];
                    wanted=Radius(i,outward)+SkinClearance+Mathf.Sqrt(Mathf.Max(0,pawRadius*pawRadius-beyond*beyond));
                    pawPoint=centers[i]+axes[i]*along+outward*wanted;moved=true;
                }
                if(arms!=null)
                    foreach(var part in arms)
                    {
                        Vector3 center=ClosestAxisPoint(part,pawPoint);
                        Vector3 radial=pawPoint-center;
                        float wanted=part.Radius+SkinClearance+pawRadius;
                        if(radial.sqrMagnitude>=wanted*wanted)continue;
                        Vector3 toward=(approachPoint-center).normalized;
                        Vector3 outward=radial.sqrMagnitude>.000001f?radial.normalized:toward;
                        if(Vector3.Dot(outward,toward)<-.1f)outward=toward;
                        if(outward.sqrMagnitude<.001f)outward=player.transform.forward;
                        pawPoint=center+outward*wanted;moved=true;
                    }
                if(!moved) break;
            }
            return pawPoint;
        }

        public Vector3 SweepOutside(Vector3 previousPaw,Vector3 desiredPaw,Vector3 approachPoint,float pawRadius=PawRadius)
        {
            previousPaw=ProjectOutside(previousPaw,approachPoint,pawRadius);
            for(int step=1;step<=SweepSamples;step++)
            {
                Vector3 sample=Vector3.Lerp(previousPaw,desiredPaw,(float)step/SweepSamples);
                Vector3 projected=ProjectOutside(sample,approachPoint,pawRadius);
                if((projected-sample).sqrMagnitude>.000001f) return projected;
            }
            return desiredPaw;
        }

        private float Radius(int slice,Vector3 direction)
        {
            float angle=Mathf.Atan2(Vector3.Dot(direction,fronts[slice]),Vector3.Dot(direction,sides[slice]));
            float sector=Mathf.Repeat(angle/(Mathf.PI*2),1)*SectorCount;
            int first=Mathf.FloorToInt(sector)%SectorCount;
            return Mathf.Lerp(outerRadii[slice*SectorCount+first],outerRadii[slice*SectorCount+(first+1)%SectorCount],sector-Mathf.Floor(sector));
        }

        private void CapsuleAxis(out Vector3 a,out Vector3 b,out float radius)
        {
            if(capsule==null)
            { a=player.Position+Vector3.up*.65f;b=player.Position+Vector3.up*1.25f;radius=.35f;return; }
            Vector3 scale=capsule.transform.lossyScale;int axis=capsule.direction;
            float axial=Mathf.Abs(scale[axis]);float side=Mathf.Max(Mathf.Abs(scale[(axis+1)%3]),Mathf.Abs(scale[(axis+2)%3]));
            radius=capsule.radius*side+UnmeasuredSkinAllowance;
            float half=Mathf.Max(0,capsule.height*axial*.5f-radius);
            Vector3 direction=capsule.transform.TransformDirection(axis==0?Vector3.right:axis==1?Vector3.up:Vector3.forward);
            Vector3 center=capsule.transform.TransformPoint(capsule.center);
            a=center-direction*half;b=center+direction*half;
        }
    }
}
