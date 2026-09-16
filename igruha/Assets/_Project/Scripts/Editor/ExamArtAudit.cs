using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Igruha.Core.Arena;
using Igruha.Minigames.Exam;

namespace Igruha.EditorTools
{
    /// <summary>Checks functional geometry, wiring and provenance of the redesigned hall.</summary>
    internal static class ExamArtAudit
    {
        [MenuItem("Igruha/Экзамен/Замеры арта")]
        internal static void Measure()
        {
            var arena=GameObject.Find("_Arena");if(arena==null)throw new InvalidOperationException("Open Exam first");
            var report=new StringBuilder("Exam Hall functional art audit\n");int failures=0;
            Action<bool,string> check=(ok,label)=>{report.AppendLine((ok?"PASS ":"FAIL ")+label);if(!ok)failures++;};
            check(arena.transform.Find("OriginalHall")!=null,"original Blender art present");
            var game=UnityEngine.Object.FindFirstObjectByType<ExamMinigame>(FindObjectsInactive.Include);
            var so=new SerializedObject(game);
            foreach(var field in new[]{"platformA","platformB","board","podiumStand","returnZone","podiumCameraRig","hallCameraRig","config","stageState","questionInput"})
                check(so.FindProperty(field).objectReferenceValue!=null,"wired "+field);
            var renderers=arena.GetComponentsInChildren<Renderer>(true);
            check(renderers.All(r=>r.sharedMaterials.All(m=>m!=null && m.shader!=null)),"all renderer materials/shaders resolved");
            var paths=AssetDatabase.GetDependencies("Assets/_Project/Scenes/Minigames/Exam.unity",true);
            var purchased=paths.Where(p=>p.Contains("/Synty/")||p.Contains("/Polygon")).ToArray();
            check(purchased.Length==0,"no Synty/Polygon scene or runtime dependencies ("+purchased.Length+")");
            foreach(var path in purchased)report.AppendLine(path);
            Physics.SyncTransforms();int mask=LayerMask.GetMask("Ground","Cover","PlayerBarrier");
            var a=arena.transform.Find("Platform_A");var b=arena.transform.Find("Platform_B");
            foreach(var platform in new[]{a,b})
            {
                check(platform.GetComponent<ExamAnswerPlatform>().Contains(platform.position+Vector3.up),platform.name+" contains player centre");
                foreach(string leafName in new[]{"DoorLeft","DoorRight"})
                {
                    var door=platform.Find(leafName);var col=door.GetComponentsInChildren<Collider>();
                    check(col.Length==1 && col[0].gameObject.layer==LayerMask.NameToLayer("Ground"),platform.name+" "+leafName+" single authoritative floor collider");
                    var visual=door.GetComponentsInChildren<MeshRenderer>().First(r=>r.name.StartsWith("EH_DoorLeaf"));
                    check(Mathf.Abs(visual.bounds.size.x-2.87f)<.03f && Mathf.Abs(visual.bounds.size.z-4.31f)<.03f,"Blender leaf metres/orientation match collider");
                }
                var mechanism=platform.GetComponent<ExamHatchMechanism>();
                check(mechanism!=null && mechanism.Rams.Length==4 && mechanism.LeftVisual!=null && mechanism.RightVisual!=null,platform.name+" four driven rams and both visual pivots");
                check(mechanism!=null && mechanism.Papers!=null && mechanism.Steam!=null,platform.name+" release VFX wired");
                check(mechanism!=null && mechanism.Rams.All(r=>r.Leaf!=null && r.Barrel!=null && r.Rod!=null && r.Gear!=null),platform.name+" all mechanism links resolved");
                RaycastHit hit;
                check(Physics.Raycast(platform.position+Vector3.up*2,Vector3.down,out hit,3,mask,QueryTriggerInteraction.Ignore) && hit.collider.name=="Leaf",platform.name+" standable when closed");
            }
            // Sweep a full capsule-sized route between both centres, along the safe gap.
            for(float x=-3.6f;x<=3.6f;x+=.6f)
            {
                var p=new Vector3(x,.10f,a.position.z);
                check(!Physics.CheckCapsule(p+Vector3.up*.42f,p+Vector3.up*1.42f,.32f,mask,QueryTriggerInteraction.Ignore),"clear crossing x="+x.ToString("F1"));
            }
            var spawns=UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(t=>t.name.StartsWith("Spawn_Student_")).ToArray();
            check(spawns.Length==8,"eight student spawn markers");
            foreach(var t in spawns)
            {
                Vector3 p=t.position;
                check(!Physics.CheckCapsule(p+Vector3.up*.45f,p+Vector3.up*1.40f,.36f,mask,QueryTriggerInteraction.Ignore),t.name+" clear capsule");
                check(Physics.Raycast(p+Vector3.up,Vector3.down,2,mask,QueryTriggerInteraction.Ignore),t.name+" has floor");
            }
            foreach(var spawn in spawns)foreach(var target in new[]{a,b})
            {
                bool clear=true;
                for(int i=0;i<=24;i++)
                {
                    var p=Vector3.Lerp(spawn.position,target.position+Vector3.up*.1f,i/24f);
                    if(Physics.CheckCapsule(p+Vector3.up*.45f,p+Vector3.up*1.40f,.36f,mask,QueryTriggerInteraction.Ignore))clear=false;
                }
                check(clear,spawn.name+" direct walk to "+target.name);
            }
            var fx=arena.GetComponent<ExamEffects>();var fxso=new SerializedObject(fx);
            foreach(var side in new[]{"sideA","sideB"})foreach(var field in new[]{"Platform","SeamDust","RimDust","CloseDust"})
                check(fxso.FindProperty(side).FindPropertyRelative(field).objectReferenceValue!=null,"effect wired "+side+"."+field);
            check(arena.transform.Find("Effects/Pit").position==Vector3.zero,"sound anchor does not move pit effects");
            check(arena.transform.Find("OriginalHall").GetComponentsInChildren<Collider>(true).All(c=>c.gameObject.layer==LayerMask.NameToLayer("Cover")),"accessible furniture collision uses Cover");
            int tris=0;foreach(var f in arena.GetComponentsInChildren<MeshFilter>(true))if(f.sharedMesh!=null)for(int i=0;i<f.sharedMesh.subMeshCount;i++)tris+=(int)f.sharedMesh.GetIndexCount(i)/3;
            report.AppendLine("renderers="+renderers.Length+" triangles="+tris+" materials="+renderers.SelectMany(r=>r.sharedMaterials).Distinct().Count()+" colliders="+arena.GetComponentsInChildren<Collider>(true).Length);
            report.AppendLine("failures="+failures);
            Directory.CreateDirectory("../docs/art/exam");File.WriteAllText("../docs/art/exam/audit.txt",report.ToString());
            if(failures>0)Debug.LogError(report.ToString());else Debug.Log(report.ToString());
        }
    }
}
