const bool cageReview=true;
UnityEngine.Random.InitState(1310);
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
 var face=bear.GetType().GetMethod("TurnToward",flags);var pose=motion.GetType().GetMethod("EvaluatePose",flags);
 bear.Configure(8.2f,.85f,2.35f,3,8,8.64f);
 animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.Rebind();animator.SetFloat("Speed",0);animator.Play("Locomotion",0,0);animator.Update(0);
 var skinsList=new System.Collections.Generic.List<SkinnedMeshRenderer>(go.GetComponentsInChildren<SkinnedMeshRenderer>());
 Igruha.Core.Player.PlayerController player=null;Animator playerAnimator=null;
 if(cageReview){
  var cageSource=sourceScene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Igruha.Minigames.Circus.CageStation>(true)).First(c=>c.name=="Cage_05");
  var cage=UnityEngine.Object.Instantiate(cageSource.gameObject);preview.AddSingleGO(cage);cage.transform.position=new Vector3(0,4.2f,-7.2f);
  foreach(var behaviour in cage.GetComponentsInChildren<MonoBehaviour>(true))behaviour.enabled=false;
  var avatar=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player/Player.prefab"));preview.AddSingleGO(avatar);
  foreach(var behaviour in avatar.GetComponentsInChildren<MonoBehaviour>(true))behaviour.enabled=false;
  avatar.transform.SetPositionAndRotation(new Vector3(0,4.35f,-7.2f),Quaternion.identity);
  var body=avatar.GetComponent<Rigidbody>();body.isKinematic=true;body.position=avatar.transform.position;
  player=avatar.GetComponent<Igruha.Core.Player.PlayerController>();playerAnimator=avatar.GetComponentInChildren<Animator>();playerAnimator.cullingMode=AnimatorCullingMode.AlwaysAnimate;playerAnimator.Rebind();playerAnimator.Play("Idle",0,0);playerAnimator.Update(0);
  // Player keeps its native humanoid skin renderer; bear poses are baked for deterministic preview.
 }
 var skins=skinsList.ToArray();
 foreach(var skin in skins){var baked=new GameObject("Review mesh");baked.transform.SetParent(skin.transform,false);var filter=baked.AddComponent<MeshFilter>();var mesh=new Mesh();meshes.Add(mesh);filter.sharedMesh=mesh;baked.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;skin.enabled=false;}
 var floorMat=new Material(Shader.Find("Universal Render Pipeline/Lit"));floorMat.color=new Color(.16f,.19f,.22f);mats.Add(floorMat);
 var lineMat=new Material(floorMat);lineMat.color=new Color(.26f,.30f,.33f);mats.Add(lineMat);
 var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(0,-.065f,0);floor.transform.localScale=new Vector3(14,.1f,14);floor.GetComponent<Renderer>().sharedMaterial=floorMat;preview.AddSingleGO(floor);
 for(int i=-6;i<=6;i++)for(int axis=0;axis<2;axis++){var line=GameObject.CreatePrimitive(PrimitiveType.Cube);line.transform.position=axis==0?new Vector3(i,-.012f,0):new Vector3(0,-.012f,i);line.transform.localScale=axis==0?new Vector3(.012f,.004f,14):new Vector3(14,.004f,.012f);line.GetComponent<Renderer>().sharedMaterial=lineMat;preview.AddSingleGO(line);}
 preview.camera.transform.position=cageReview?new Vector3(-11,10,9):new Vector3(8,6,8);preview.camera.transform.LookAt(cageReview?new Vector3(0,2,-2.5f):new Vector3(1.1f,.8f,0));preview.camera.orthographic=true;preview.camera.orthographicSize=cageReview?7f:3.8f;
 preview.camera.nearClipPlane=.01f;preview.camera.farClipPlane=100;preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=new Color(.10f,.12f,.15f);
 preview.lights[0].intensity=1.6f;preview.lights[0].transform.rotation=Quaternion.Euler(50,210,0);preview.lights[1].intensity=.9f;preview.lights[1].transform.rotation=Quaternion.Euler(35,100,0);preview.ambientColor=new Color(.3f,.3f,.3f);
 var output=System.IO.Path.GetFullPath(cageReview?"../art-source/BrunoMeshy/cage-arcs/cage-frames":"../art-source/BrunoMeshy/cage-arcs/arc-frames");System.IO.Directory.CreateDirectory(output);
 const float dt=1f/60;
 var occupants=new[]{player};
 var samples=new System.Collections.Generic.List<string>();
 for(int frame=0;frame<(cageReview?420:210);frame++){
  for(int tick=0;tick<2;tick++){var before=go.transform.position;if(cageReview)bear.Tick(dt,null,false,occupants);else face.Invoke(bear,new object[]{Vector3.back,dt});
   animator.SetFloat("Speed",cageReview?bear.AnimatorSpeed:Vector3.Distance(before,go.transform.position)/dt,.13f,dt);animator.Update(dt);if(playerAnimator!=null)playerAnimator.Update(dt);pose.Invoke(motion,new object[]{dt});}
  if(frame%30==0)samples.Add(frame/30f+" "+bear.State+" "+bear.transform.position.ToString("F2"));
  for(int i=0;i<skins.Length;i++)skins[i].BakeMesh(meshes[i]);
  preview.BeginStaticPreview(new Rect(0,0,1280,900));preview.Render(true);var image=preview.EndStaticPreview();System.IO.File.WriteAllBytes(System.IO.Path.Combine(output,"frame_"+frame.ToString("D3")+".png"),image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
 }
 return samples;
}finally{preview.Cleanup();foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);foreach(var mat in mats)UnityEngine.Object.DestroyImmediate(mat);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(sourceScene);}
