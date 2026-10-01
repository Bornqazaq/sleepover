using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Igruha.Core.Items;
using Igruha.Minigames.CarryItem;

namespace Igruha.EditorTools
{
    /// <summary>Geometry acceptance for the road layout, including four real handle stations.</summary>
    internal static class CarryRoadArenaAudit
    {
        private const float SampleStep=.2f, PlayerRadius=.36f, PlayerHeight=1.65f;
        [MenuItem("Igruha/Minigames/Audit Carry Item Roads")]
        internal static string Run()
        {
            Physics.SyncTransforms();
            var report=new StringBuilder();var problems=new HashSet<string>();int samples=0;
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Minigames/CarryItem/WaterCart.prefab");
            MultiCarrySettings settings=prefab.GetComponent<MultiCarryObject>().Settings;
            int cover=LayerMask.GetMask("Cover"),ground=LayerMask.GetMask("Ground");
            foreach(int sign in new[]{-1,1})
            {
                Sweep(new[]{new Vector3(-3.2f,0,sign*1.35f),new Vector3(7.2f,0,sign*1.35f)},"shortcut",settings,cover,ground,problems,ref samples);
                Sweep(new[]{new Vector3(-2.7f,0,sign*5.04f),new Vector3(-2.7f,0,sign*11.5f),
                    new Vector3(6.9f,0,sign*11.5f),new Vector3(6.9f,0,sign*5.04f)},"bypass",settings,cover,ground,problems,ref samples);
                Sweep(new[]{new Vector3(-12.4f,.288f,sign*5.04f),new Vector3(-6.2f,.288f,sign*5.04f)},"bridge1",settings,cover,ground,problems,ref samples);
                Sweep(new[]{new Vector3(10.7f,.288f,sign*5.04f),new Vector3(15.2f,.288f,sign*5.04f)},"bridge2",settings,cover,ground,problems,ref samples);
            }
            var joints=Object.FindObjectsByType<CartRoadJoint>(FindObjectsSortMode.None);
            foreach(var j in joints)
            {
                var r=j.GetComponentInChildren<Renderer>();
                if(r==null || r.bounds.size.z<j.Width*.97f)problems.Add("Invisible/undersized joint "+j.transform.position);
            }
            var root=GameObject.Find("_Arena/Roadworks");
            var all=Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
            foreach(var a in root.GetComponentsInChildren<Collider>())foreach(var b in all)
            {
                if(b==a||b.isTrigger||b.transform.IsChildOf(root.transform)||b.gameObject.layer!=LayerMask.NameToLayer("Cover"))continue;
                Bounds bounds=a.bounds;bounds.Expand(-.04f);
                if(bounds.Intersects(b.bounds))problems.Add(a.name+" intersects "+b.name+" at "+a.transform.position);
            }
            report.Append("Carry road audit: ").Append(samples).Append(" footprints, joints=").Append(joints.Length).Append(", problems=").Append(problems.Count);
            foreach(var p in problems)report.Append('\n').Append(p);
            if(joints.Length!=14)report.Append("\nExpected 14 authored joints");
            Debug.Log(report.ToString());return report.ToString();
        }
        private static void Sweep(Vector3[] points,string name,MultiCarrySettings settings,int cover,int ground,HashSet<string> problems,ref int samples)
        {
            for(int i=1;i<points.Length;i++)
            {
                Vector3 d=points[i]-points[i-1];Quaternion q=Quaternion.LookRotation(d);int count=Mathf.CeilToInt(d.magnitude/SampleStep);
                for(int k=0;k<=count;k++)
                    Footprint(Vector3.Lerp(points[i-1],points[i],(float)k/count),q,name,settings,cover,ground,problems,ref samples);
                // Clearance to turn the four stations through the corner.
                if(i<points.Length-1)
                    for(int k=1;k<=8;k++)Footprint(points[i],Quaternion.Slerp(q,Quaternion.LookRotation(points[i+1]-points[i]),k/8f),name+" corner",settings,cover,ground,problems,ref samples);
            }
        }
        private static void Footprint(Vector3 p,Quaternion q,string name,MultiCarrySettings settings,int cover,int ground,HashSet<string> problems,ref int samples)
        {
            samples++;
            foreach(var c in Physics.OverlapBox(p+Vector3.up*.55f,new Vector3(.5f,.48f,.67f),q,cover,QueryTriggerInteraction.Ignore))problems.Add(name+" cart hits "+c.name);
            for(int i=0;i<4;i++)
            {
                Vector3 station=p+q*new Vector3(i%2==0?-settings.cartHandleSide:settings.cartHandleSide,0,
                    i<2?-settings.cartHandleBack-settings.carrierStandoff:settings.cartHandleFront+settings.carrierStandoff);
                foreach(var c in Physics.OverlapCapsule(station+Vector3.up*(PlayerRadius+.025f),station+Vector3.up*(PlayerHeight-PlayerRadius),PlayerRadius,cover,QueryTriggerInteraction.Ignore))
                    problems.Add(name+" carrier hits "+c.name);
                if(!Physics.Raycast(station+Vector3.up*.45f,Vector3.down,.8f,ground,QueryTriggerInteraction.Ignore))problems.Add(name+" carrier over void");
            }
        }
    }
}
