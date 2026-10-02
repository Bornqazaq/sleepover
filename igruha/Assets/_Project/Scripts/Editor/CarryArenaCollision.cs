using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Igruha.Minigames.CarryItem;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Physical bodies of the accessible site, not paint/water/sky or duplicate floor skins.</summary>
    internal static class CarryArenaCollision
    {
        internal static void Apply(Transform arena)
        {
            AddMissing(arena.Find("Environment/WorkAreas"));
            AddMissing(arena.Find("Environment/Structure"));
            AddMissing(arena.Find("Environment/Decor"));
            foreach(Transform t in arena.Find("Environment/Horizon"))if(ReachableBackground(t))AddModel(t);
            foreach(var t in arena.Find("Roadworks").GetComponentsInChildren<Transform>())
                if((t.name.StartsWith("RW_",StringComparison.Ordinal)||t.name.StartsWith("CS_",StringComparison.Ordinal))&&t.childCount>0)AddModel(t);
            var heist=arena.Find("HeistRoutes");
            foreach(var t in heist.GetComponentsInChildren<Transform>())
            {
                if(t==null||t.childCount==0)continue;
                if(!t.name.StartsWith("HR_",StringComparison.Ordinal)&&!t.name.StartsWith("RW_",StringComparison.Ordinal))continue;
                if(t.name=="HR_PrecastDeck"||t.name=="HR_Deck"||t.name=="HR_SideDeck"||t.name=="HR_SuspendedBeam")continue;
                if(t.name=="HR_Trestle")
                {
                    for(int i=t.childCount-1;i>=0;i--)if(t.GetChild(i).name=="Leg")Object.DestroyImmediate(t.GetChild(i).gameObject);
                }
                AddModel(t);
            }
            // Pump frame and tap hardware are solid; moving hose/water remain presentation.
            foreach(Transform team in arena.Find("TeamProps"))
                foreach(var t in team.GetComponentsInChildren<Transform>())
                    if(t.name=="Standpipe"||t.name=="PipeSpout"||t.name=="PumpBody")AddModel(t);
            Physics.SyncTransforms();
        }
        private static void AddMissing(Transform parent)
        {if(parent==null)return;foreach(Transform t in parent)if(!IsPresentation(t.name))AddModel(t);}
        private static bool IsPresentation(string name)=>name.Contains("Puddle")||name.Contains("Bunting")||name.StartsWith("CS_Arrow",StringComparison.Ordinal)||name.Contains("TeamFlag");
        private static void AddModel(Transform root)
        {
            if(IsPresentation(root.name))return;
            if(root.name=="RW_Joint"||root.name=="RW_RepairPlate"||root.name=="RW_Drain")
            {
                // These are skins bolted into the slab, not vertical steps for the chassis.
                // Their physical backing sits below the slab top. Wheel strikes remain authored
                // by CartRoadJoint; duplicate raised mesh faces previously stopped a cart dead.
                foreach(var c in root.GetComponentsInChildren<Collider>())Object.DestroyImmediate(c);
                var b=CarryItemDress.BoundsOf(root.gameObject);var floor=root.gameObject.AddComponent<BoxCollider>();
                floor.center=new Vector3(0,-root.position.y-.14f,0);floor.size=new Vector3(b.size.x,.20f,b.size.z);
                root.gameObject.layer=LayerMask.NameToLayer("Ground");return;
            }
            // Existing deliberate simple envelopes win. Meshes only fill genuinely missing bodies.
            foreach(var c in root.GetComponentsInChildren<Collider>())if(c.enabled&&!c.isTrigger)return;
            foreach(var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                if(filter.sharedMesh==null||filter.GetComponent<Renderer>()==null||!filter.GetComponent<Renderer>().enabled)continue;
                bool covered=false;var t=filter.transform;
                while(t!=null)
                {
                    foreach(var c in t.GetComponents<Collider>())if(c.enabled&&!c.isTrigger)covered=true;
                    if(t==root)break;t=t.parent;
                }
                if(covered)continue;
                var collider=filter.gameObject.AddComponent<MeshCollider>();collider.sharedMesh=filter.sharedMesh;
                filter.gameObject.layer=LayerMask.NameToLayer(root.name=="RW_Joint"||root.name=="RW_RepairPlate"||root.name=="RW_Drain"?"Ground":"Cover");
            }
        }
        private static bool ReachableBackground(Transform root)
        {
            var renderers=root.GetComponentsInChildren<Renderer>();if(renderers.Length==0)return false;
            var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
            var playable=new Bounds(new Vector3(0,3.35f,0),new Vector3(56,6.5f,56));return bounds.Intersects(playable);
        }
        internal static string Audit(Transform arena)
        {
            var missing=new List<string>();int models=0;
            foreach(string path in new[]{"Environment/WorkAreas","Environment/Structure","Environment/Decor","Environment/Horizon","Roadworks","HeistRoutes"})
            {
                var group=arena.Find(path);if(group==null)continue;
                foreach(var t in group.GetComponentsInChildren<Transform>())
                {
                    if(!(t.name.StartsWith("CS_",StringComparison.Ordinal)||t.name.StartsWith("RW_",StringComparison.Ordinal)||t.name.StartsWith("HR_",StringComparison.Ordinal)))continue;
                    if(t.childCount==0||IsPresentation(t.name)||t.name=="HR_PrecastDeck"||t.name=="HR_Deck"||t.name=="HR_SideDeck")continue;
                    // Wrappers own models. Imported nested meshes are covered by the wrapper collider.
                    if(t.parent!=null&&(t.parent.name==t.name||t.parent.name.StartsWith("CS_",StringComparison.Ordinal)||t.parent.name.StartsWith("RW_",StringComparison.Ordinal)||t.parent.name.StartsWith("HR_",StringComparison.Ordinal)))continue;
                    if(path=="Environment/Horizon"&&!ReachableBackground(t))continue;
                    models++;bool solid=false;
                    foreach(var c in t.GetComponentsInChildren<Collider>())if(c.enabled&&!c.isTrigger)solid=true;
                    var p=t.parent;while(!solid&&p!=null&&p!=arena){foreach(var c in p.GetComponents<Collider>())if(c.enabled&&!c.isTrigger)solid=true;p=p.parent;}
                    if(!solid)missing.Add(path+"/"+t.name+" at "+t.position);
                }
            }
            return "Collision bodies checked="+models+", missing="+missing.Count+(missing.Count==0?"":"\n"+string.Join("\n",missing));
        }
    }
}
