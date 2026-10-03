using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Igruha.Core.Items;
using Igruha.Minigames.CarryItem;

namespace Igruha.EditorTools
{
    /// <summary>All three delivery routes, turns, four stations and complete physical prop coverage.</summary>
    internal static class CarryRoadArenaAudit
    {
        private const float SampleStep=.2f, PlayerRadius=.36f, PlayerHeight=1.65f;
        [MenuItem("Igruha/Minigames/Audit Carry Item Roads")]
        internal static string Run()
        {
            Physics.SyncTransforms();var report=new StringBuilder();var problems=new HashSet<string>();int samples=0;
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Minigames/CarryItem/WaterCart.prefab");
            MultiCarrySettings settings=prefab.GetComponent<MultiCarryObject>().Settings;
            foreach(int side in new[]{-1,1})for(int route=0;route<3;route++)
            {
                var points=CarryRouteLayout.Delivery(route,side);
                Sweep(points,"route "+route+" side "+side,settings,problems,ref samples);
                System.Array.Reverse(points);Sweep(points,"return "+route+" side "+side,settings,problems,ref samples);
                if(side==1)report.Append("Route ").Append(route).Append(" length=").Append(CarryRouteLayout.Length(points).ToString("F1")).Append("m\n");
            }
            var joints=Object.FindObjectsByType<CartRoadJoint>(FindObjectsSortMode.None);
            foreach(var j in joints)
            {
                var r=j.GetComponentInChildren<Renderer>();
                if(r==null)problems.Add("Invisible joint "+j.transform.position);
                if(r!=null)
                {
                    var bounds=r.localBounds;float min=float.MaxValue,max=float.MinValue;
                    for(int corner=0;corner<8;corner++)
                    {
                        var p=bounds.center+Vector3.Scale(bounds.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1));
                        float z=j.transform.InverseTransformPoint(r.transform.TransformPoint(p)).z;min=Mathf.Min(min,z);max=Mathf.Max(max,z);
                    }
                    if(Mathf.Abs(max-min-j.Width)>.04f)problems.Add("Joint model does not fit road: "+j.transform.position);
                }
                if(Mathf.Abs(j.transform.position.z)>7)problems.Add("Rough joint on safe route "+j.transform.position);
            }
            if(joints.Length!=14)problems.Add("Expected 14 visible rough-route joints, found "+joints.Length);
            var patches=Object.FindObjectsByType<CartRoughSurface>(FindObjectsSortMode.None);
            if(patches.Length!=8)problems.Add("Expected 8 broken concrete patches, found "+patches.Length);
            foreach(var patch in patches)
            {
                if(patch.GetComponent<Renderer>()==null||patch.GetComponent<Collider>()==null)problems.Add("Invisible or unsupported rough patch");
                if(Mathf.Abs(patch.transform.position.z)>7)problems.Add("Rough concrete on safe route");
            }
            foreach(var mesh in GameObject.Find("_Arena/SiteFinish").GetComponentsInChildren<MeshFilter>())
            {
                if(mesh.name.Contains("Paint")||mesh.name=="Concrete cracks"||mesh.name=="Aggregate flecks")continue;
                if(mesh.GetComponent<Collider>()==null)problems.Add("Missing site collision: "+mesh.name);
            }
            report.Append("Rough concrete patches=").Append(patches.Length).Append('\n');
            foreach(var rail in GameObject.Find("_Arena/HeistRoutes/LowerRoutes").GetComponentsInChildren<Renderer>())
                if(rail.name=="HR_Rail"&&rail.bounds.size.y<1f)problems.Add("Undersized gallery railing "+rail.transform.position);
            int seated=0;
            foreach(Transform prop in GameObject.Find("_Arena/Environment/Horizon").transform)
            {
                if(prop.position.y>=-.5f||prop.position.y<-36)continue;
                if(prop.name!="CS_CementBags"&&prop.name!="CS_WeldCart"&&prop.name!="CS_Tarp"&&prop.name!="CS_Lumber"&&
                    prop.name!="CS_Formwork"&&prop.name!="CS_FloodTower"&&prop.name!="CS_Scaffold")continue;
                var b=CarryItemDress.BoundsOf(prop.gameObject);seated++;
                foreach(float x in new[]{b.min.x+.03f,b.max.x-.03f})foreach(float z in new[]{b.min.z+.03f,b.max.z-.03f})
                    if(!Physics.Raycast(new Vector3(x,b.min.y+.035f,z),Vector3.down,.10f,LayerMask.GetMask("Cover","Ground"),QueryTriggerInteraction.Ignore))
                        problems.Add("Unsupported lower prop "+prop.name+" "+prop.position);
            }
            report.Append("Lower props seated=").Append(seated).Append('\n');
            report.Append("Carry road audit: ").Append(samples).Append(" footprints, joints=").Append(joints.Length).Append(", problems=").Append(problems.Count);
            foreach(var p in problems)report.Append('\n').Append(p);
            report.Append('\n').Append(CarryArenaCollision.Audit(GameObject.Find("_Arena").transform));
            Debug.Log(report.ToString());return report.ToString();
        }
        private static void Sweep(Vector3[] points,string name,MultiCarrySettings settings,HashSet<string> problems,ref int samples)
        {
            for(int i=1;i<points.Length;i++)
            {
                Vector3 d=points[i]-points[i-1];d.y=0;Quaternion q=Quaternion.LookRotation(d);int count=Mathf.CeilToInt(Vector3.Distance(points[i],points[i-1])/SampleStep);
                for(int k=0;k<=count;k++)Footprint(Vector3.Lerp(points[i-1],points[i],(float)k/count),q,name,settings,problems,ref samples);
                if(i<points.Length-1)
                {
                    Vector3 next=points[i+1]-points[i];next.y=0;
                    for(int k=1;k<=8;k++)Footprint(points[i],Quaternion.Slerp(q,Quaternion.LookRotation(next),k/8f),name+" corner",settings,problems,ref samples);
                }
            }
        }
        private static void Footprint(Vector3 p,Quaternion q,string name,MultiCarrySettings settings,HashSet<string> problems,ref int samples)
        {
            samples++;int cover=LayerMask.GetMask("Cover"),ground=LayerMask.GetMask("Ground");
            if(Physics.Raycast(p+Vector3.up*.9f,Vector3.down,out var floor,2,ground,QueryTriggerInteraction.Ignore))p.y=floor.point.y;
            else problems.Add(name+" cart unsupported near "+p.ToString("F1"));
            foreach(var c in Physics.OverlapBox(p+Vector3.up*.65f,new Vector3(.5f,.43f,.67f),q,cover,QueryTriggerInteraction.Ignore))
                if(c.GetComponentInParent<CarryCraneHazard>()==null)problems.Add(name+" cart hits "+c.name+" at "+c.transform.position.ToString("F1"));
            for(int i=0;i<4;i++)
            {
                Vector3 station=p+q*new Vector3(i%2==0?-settings.cartHandleSide:settings.cartHandleSide,0,
                    i<2?-settings.cartHandleBack-settings.carrierStandoff:settings.cartHandleFront+settings.carrierStandoff);
                if(Physics.Raycast(station+Vector3.up*.9f,Vector3.down,out floor,2,ground,QueryTriggerInteraction.Ignore))station.y=floor.point.y+.03f;
                else problems.Add(name+" carrier over void near "+station.ToString("F1"));
                foreach(var c in Physics.OverlapCapsule(station+Vector3.up*PlayerRadius,station+Vector3.up*(PlayerHeight-PlayerRadius),PlayerRadius,cover,QueryTriggerInteraction.Ignore))
                    if(c.GetComponentInParent<CarryCraneHazard>()==null)problems.Add(name+" carrier hits "+c.name+" at "+c.transform.position.ToString("F1"));
            }
        }
    }
}
