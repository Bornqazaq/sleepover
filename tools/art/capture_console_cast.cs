// Execute through Unity MCP execute_code; replace CAST_CHARACTER with a CharacterRoster name.
// Only temporary preview instances are rendered. Set outputFolder before running.
var outputFolder = System.IO.Path.Combine(System.IO.Directory.GetParent(UnityEngine.Application.dataPath).Parent.FullName, "tools/art/references/console-cast");
System.IO.Directory.CreateDirectory(outputFolder);
var roster=UnityEditor.AssetDatabase.LoadAssetAtPath<Igruha.Core.Player.CharacterRoster>("Assets/_Project/Settings/Gameplay/CharacterRoster.asset");
var entries=new UnityEditor.SerializedObject(roster).FindProperty("characters");
var element=Enumerable.Range(0,entries.arraySize).Select(i=>entries.GetArrayElementAtIndex(i)).First(e=>e.FindPropertyRelative("displayName").stringValue=="CAST_CHARACTER");
var prefab=(UnityEngine.GameObject)element.FindPropertyRelative("prefab").objectReferenceValue;
var preview=new UnityEditor.PreviewRenderUtility();
try {
 preview.ambientColor=new UnityEngine.Color(.55f,.55f,.55f,1);
 preview.camera.clearFlags=UnityEngine.CameraClearFlags.SolidColor;
 preview.camera.backgroundColor=new UnityEngine.Color(.19f,.23f,.25f,1);
 preview.camera.orthographic=true;preview.camera.orthographicSize=1.55f;
 preview.camera.nearClipPlane=.1f;preview.camera.farClipPlane=30f;
 preview.camera.transform.position=new UnityEngine.Vector3(0,1.45f,7);
 preview.camera.transform.LookAt(new UnityEngine.Vector3(0,1.35f,0));
 preview.lights[0].type=UnityEngine.LightType.Directional;preview.lights[0].intensity=1.25f;preview.lights[0].color=UnityEngine.Color.white;preview.lights[0].transform.eulerAngles=new UnityEngine.Vector3(35,200,0);
 preview.lights[1].type=UnityEngine.LightType.Directional;preview.lights[1].intensity=.65f;preview.lights[1].color=new UnityEngine.Color(.85f,.93f,1);preview.lights[1].transform.eulerAngles=new UnityEngine.Vector3(15,115,0);
 var models=new System.Collections.Generic.List<UnityEngine.Renderer>();
 for(int i=0;i<2;i++){
  var instance=UnityEngine.Object.Instantiate(prefab);preview.AddSingleGO(instance);
  instance.hideFlags=UnityEngine.HideFlags.HideAndDontSave;
  foreach(var behaviour in instance.GetComponentsInChildren<UnityEngine.MonoBehaviour>(true))behaviour.enabled=false;
  var animator=instance.GetComponentInChildren<UnityEngine.Animator>();
  var idle=animator.runtimeAnimatorController.animationClips.First(c=>c.name.ToLowerInvariant().Contains("idle"));
  animator.Rebind();idle.SampleAnimation(animator.gameObject,0.25f);animator.enabled=false;
  instance.transform.SetPositionAndRotation(new UnityEngine.Vector3(i==0?-.95f:.95f,0,0),UnityEngine.Quaternion.Euler(0,i==0?0:40,0));
  models.AddRange(instance.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>());
 }
 var bounds=models[0].bounds;foreach(var model in models)bounds.Encapsulate(model.bounds);
 preview.camera.orthographicSize=UnityEngine.Mathf.Max(bounds.extents.y,bounds.extents.x/1.5f)*1.1f;
 preview.camera.transform.position=bounds.center+new UnityEngine.Vector3(0,.05f,7);
 preview.camera.transform.LookAt(bounds.center);
 preview.BeginStaticPreview(new UnityEngine.Rect(0,0,1536,1024));
 preview.Render(true,false);
 var image=preview.EndStaticPreview();
 var path=System.IO.Path.Combine(outputFolder,"CAST_CHARACTER.png");
 System.IO.File.WriteAllBytes(path,image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
 return path;
} finally {preview.Cleanup();}
