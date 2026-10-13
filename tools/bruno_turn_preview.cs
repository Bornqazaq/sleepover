var sourceScene=UnityEditor.SceneManagement.EditorSceneManager.OpenPreviewScene("Assets/_Project/Scenes/Minigames/CansOrder.unity");
var preview=new UnityEditor.PreviewRenderUtility();
var meshes=new System.Collections.Generic.List<Mesh>();
var mats=new System.Collections.Generic.List<Material>();
try {
 var source=sourceScene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Igruha.Minigames.Circus.PitBear>(true)).First(b=>b.name=="PitBear");
 var go=UnityEngine.Object.Instantiate(source.gameObject);preview.AddSingleGO(go);go.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
 var bear=go.GetComponent<Igruha.Minigames.Circus.PitBear>();var motion=go.GetComponentInChildren<Igruha.Minigames.Circus.CircusBearMotion>();var animator=go.GetComponentInChildren<Animator>();
 var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
 bear.GetType().GetMethod("Awake",flags).Invoke(bear,null);motion.GetType().GetMethod("Awake",flags).Invoke(motion,null);
 var face=bear.GetType().GetMethod("FaceTowards",flags);var pose=motion.GetType().GetMethod("EvaluatePose",flags);
 bear.ApplyNetworkState(Igruha.Minigames.Circus.PitBear.BearState.Watching,0);
 animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.Rebind();animator.SetFloat("Speed",0);animator.Play("Locomotion",0,0);animator.Update(0);
 var skins=go.GetComponentsInChildren<SkinnedMeshRenderer>();
 foreach(var skin in skins){var baked=new GameObject("Review mesh");baked.transform.SetParent(skin.transform,false);var filter=baked.AddComponent<MeshFilter>();var mesh=new Mesh();meshes.Add(mesh);filter.sharedMesh=mesh;baked.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;skin.enabled=false;}
 var floorMat=new Material(Shader.Find("Universal Render Pipeline/Lit"));floorMat.color=new Color(.16f,.19f,.22f);mats.Add(floorMat);
 var lineMat=new Material(floorMat);lineMat.color=new Color(.26f,.30f,.33f);mats.Add(lineMat);
 var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(0,-.065f,0);floor.transform.localScale=new Vector3(14,.1f,14);floor.GetComponent<Renderer>().sharedMaterial=floorMat;preview.AddSingleGO(floor);
 for(int i=-6;i<=6;i++)for(int axis=0;axis<2;axis++){var line=GameObject.CreatePrimitive(PrimitiveType.Cube);line.transform.position=axis==0?new Vector3(i,-.012f,0):new Vector3(0,-.012f,i);line.transform.localScale=axis==0?new Vector3(.012f,.004f,14):new Vector3(14,.004f,.012f);line.GetComponent<Renderer>().sharedMaterial=lineMat;preview.AddSingleGO(line);}
 preview.camera.transform.position=new Vector3(5.5f,3.7f,6.8f);preview.camera.transform.LookAt(new Vector3(0,.8f,0));preview.camera.orthographic=true;preview.camera.orthographicSize=2.6f;
 preview.camera.nearClipPlane=.01f;preview.camera.farClipPlane=100;preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=new Color(.10f,.12f,.15f);
 preview.lights[0].intensity=1.6f;preview.lights[0].transform.rotation=Quaternion.Euler(50,210,0);preview.lights[1].intensity=.9f;preview.lights[1].transform.rotation=Quaternion.Euler(35,100,0);preview.ambientColor=new Color(.3f,.3f,.3f);
 var output=System.IO.Path.GetFullPath("../art-source/BrunoMeshy/turn-fix/frames");System.IO.Directory.CreateDirectory(output);
 const float dt=1f/60;
 for(int frame=0;frame<150;frame++){
  for(int tick=0;tick<2;tick++){face.Invoke(bear,new object[]{Vector3.back,dt});animator.Update(dt);pose.Invoke(motion,new object[]{dt});}
  for(int i=0;i<skins.Length;i++)skins[i].BakeMesh(meshes[i]);
  preview.BeginStaticPreview(new Rect(0,0,960,640));preview.Render(true);var image=preview.EndStaticPreview();System.IO.File.WriteAllBytes(System.IO.Path.Combine(output,"frame_"+frame.ToString("D3")+".png"),image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
 }
 System.IO.File.WriteAllText(System.IO.Path.GetFullPath("../art-source/BrunoMeshy/turn-fix/render.done"),"done");
 return "150 frames rendered from the actual scene bear, Animator and turn-step solver";
}finally{preview.Cleanup();foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);foreach(var mat in mats)UnityEngine.Object.DestroyImmediate(mat);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(sourceScene);}
