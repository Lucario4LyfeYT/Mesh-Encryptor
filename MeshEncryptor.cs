using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System;
using System.Collections.Generic;

public class MeshEncryptorWindow : EditorWindow
{
    private GameObject targetObject;
    private string encryptionCode = "default";
    private float encryptionOffsetMagnitude = 0.5f;
    private int amountOfBShapes = 1;
    private List<float> decryptionKeys = new List<float>() { 25f };
    private SkinnedMeshRenderer skinnedMeshRenderer;
    private class DecryptionLayerData
    {
        public string blendShapeName;
        public float realKey;
        public AnimationClip realClip;
    }

    [MenuItem("Tools/Mesh Encryptor")]
    public static void ShowWindow() { GetWindow<MeshEncryptorWindow>("Mesh Encryptor"); }

    private void OnGUI()
    {
        GUILayout.Label("Mesh Encryptor Tool", EditorStyles.boldLabel);

        targetObject = EditorGUILayout.ObjectField("Target Object", targetObject, typeof(GameObject), true) as GameObject;
        if (targetObject != null)
        {
            skinnedMeshRenderer = targetObject.GetComponent<SkinnedMeshRenderer>();
            if (skinnedMeshRenderer == null)
            {
                MeshRenderer meshRenderer = targetObject.GetComponent<MeshRenderer>();
                MeshFilter meshFilter = targetObject.GetComponent<MeshFilter>();
                if (meshRenderer != null && meshFilter != null)
                {
                    if (GUILayout.Button("Convert to SkinnedMeshRenderer"))
                    {
                        ConvertToSkinnedMeshRenderer(targetObject);
                        skinnedMeshRenderer = targetObject.GetComponent<SkinnedMeshRenderer>();
                    }
                }
                else EditorGUILayout.HelpBox("Target object must have either a SkinnedMeshRenderer or a MeshRenderer with a MeshFilter.", MessageType.Error);
            }
        }
        else { skinnedMeshRenderer = null; }

        GUILayout.Space(10);
        encryptionCode = EditorGUILayout.TextField("Encryption Code", encryptionCode);
        encryptionOffsetMagnitude = EditorGUILayout.FloatField("Offset Magnitude", encryptionOffsetMagnitude);
        amountOfBShapes = EditorGUILayout.IntField("Amount Of Blend Shapes", amountOfBShapes);
        amountOfBShapes = Mathf.Clamp(amountOfBShapes, 1, 32);

        // Adjust decryptionKeys list size to match amountOfBShapes.
        while (decryptionKeys.Count < amountOfBShapes) decryptionKeys.Add(decryptionKeys[decryptionKeys.Count - 1]);
        while (decryptionKeys.Count > amountOfBShapes) decryptionKeys.RemoveAt(decryptionKeys.Count - 1);

        // Display a decryption key field for each blend shape.
        for (int i = 0; i < amountOfBShapes; i++) 
            decryptionKeys[i] = EditorGUILayout.FloatField($"Decryption Key {i}", decryptionKeys[i]);

        if (GUILayout.Button("Encrypt Mesh and Create Animator"))
        {
            if (targetObject == null) EditorUtility.DisplayDialog("Error", "Please assign a target GameObject.", "OK");
            else if (skinnedMeshRenderer == null) EditorUtility.DisplayDialog("Error", "Target object must have a SkinnedMeshRenderer component.", "OK");
            else
            {
                List<DecryptionLayerData> layersData = new List<DecryptionLayerData>();
                for (int i = 0; i < amountOfBShapes; i++)
                {
                    DecryptionLayerData data = EncryptMeshAndGetLayerData(decryptionKeys[i]);
                    if (data != null) layersData.Add(data);
                }
                CreateCombinedAnimatorController(layersData);
            }
        }
    }

    private void ConvertToSkinnedMeshRenderer(GameObject obj)
    {
        MeshFilter meshFilter = obj.GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = obj.GetComponent<MeshRenderer>();
        if (meshFilter == null || meshRenderer == null)
        {
            EditorUtility.DisplayDialog("Conversion Error", "The selected object does not have both a MeshFilter and MeshRenderer.", "OK");
            return;
        }
        Mesh mesh = meshFilter.sharedMesh;
        if (mesh == null)
        {
            EditorUtility.DisplayDialog("Conversion Error", "The MeshFilter does not have a valid mesh.", "OK");
            return;
        }
        SkinnedMeshRenderer smr = obj.AddComponent<SkinnedMeshRenderer>();
        smr.sharedMesh = mesh;
        smr.materials = meshRenderer.sharedMaterials;
        BoneWeight[] boneWeights = new BoneWeight[mesh.vertexCount];
        for (int i = 0; i < boneWeights.Length; i++) boneWeights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
        mesh.boneWeights = boneWeights;
        smr.rootBone = obj.transform;
        smr.bones = new Transform[] { obj.transform };
        Matrix4x4 bindPose = obj.transform.worldToLocalMatrix;
        mesh.bindposes = new Matrix4x4[] { bindPose };
        DestroyImmediate(meshRenderer, true);
        DestroyImmediate(meshFilter, true);
        EditorUtility.SetDirty(mesh);
    }

    private DecryptionLayerData EncryptMeshAndGetLayerData(float decryptionKey)
    {
        Mesh originalMesh = skinnedMeshRenderer.sharedMesh;
        if (originalMesh == null)
        {
            EditorUtility.DisplayDialog("Error", "SkinnedMeshRenderer does not have a valid mesh.", "OK");
            return null;
        }
        Mesh mesh = Instantiate(originalMesh);
        mesh.name = originalMesh.name + "_Encrypted";
        skinnedMeshRenderer.sharedMesh = mesh;

        Vector3[] origVerts = mesh.vertices;
        Vector3[] origNormals = mesh.normals;
        Vector3[] encVerts = new Vector3[origVerts.Length];
        int seed = encryptionCode.GetHashCode();
        System.Random rand = new System.Random(seed);
        for (int i = 0; i < origVerts.Length; i++)
        {
            float offX = (float)(rand.NextDouble() * encryptionOffsetMagnitude - encryptionOffsetMagnitude * 0.5f);
            float offY = (float)(rand.NextDouble() * encryptionOffsetMagnitude - encryptionOffsetMagnitude * 0.5f);
            float offZ = (float)(rand.NextDouble() * encryptionOffsetMagnitude - encryptionOffsetMagnitude * 0.5f);
            encVerts[i] = origVerts[i] + new Vector3(offX, offY, offZ);
        }
        mesh.vertices = encVerts;
        mesh.RecalculateBounds(); mesh.RecalculateNormals(); mesh.RecalculateTangents();

        Vector3[] encNormals = mesh.normals;
        float scale = 100f / decryptionKey;
        Vector3[] dVerts = new Vector3[origVerts.Length];
        Vector3[] dNorms = new Vector3[origVerts.Length];
        for (int i = 0; i < origVerts.Length; i++)
        {
            dVerts[i] = (origVerts[i] - encVerts[i]) * scale;
            dNorms[i] = (origNormals[i] - encNormals[i]) * scale;
        }

        int idx = mesh.blendShapeCount;
        string name = "Decrypt" + idx;
        mesh.AddBlendShapeFrame(name, 100f, dVerts, dNorms, null);
        EditorUtility.SetDirty(mesh);

        AnimationClip clip = CreateDecryptionAnimationClip(name, decryptionKey);
        return new DecryptionLayerData { blendShapeName = name, realKey = decryptionKey, realClip = clip };
    }

    private AnimationClip CreateDecryptionAnimationClip(string blendShapeName, float targetWeight)
    {
        AnimationClip clip = new AnimationClip();
        clip.frameRate = 60f;
        var curve = new AnimationCurve(new Keyframe(0f, targetWeight));
        clip.SetCurve("", typeof(SkinnedMeshRenderer), $"blendShape.{blendShapeName}", curve);

        string path = "Assets/DecryptionAnimations";
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder("Assets", "DecryptionAnimations");

        string suffix = UnityEngine.Random.Range(1000, 9999).ToString();
        string full = System.IO.Path.Combine(path, $"MeshDecrypt_{blendShapeName}_{suffix}.anim");
        AssetDatabase.CreateAsset(clip, full);
        AssetDatabase.SaveAssets();
        return clip;
    }

    private void CreateCombinedAnimatorController(List<DecryptionLayerData> layersData)
    {
        string path = "Assets/DecryptionAnimations";
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder("Assets", "DecryptionAnimations");
        string ctrlPath = System.IO.Path.Combine(path, "CombinedDecryptionAnimator.controller");
        AnimatorController ac = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);

        for (int i = 0; i < layersData.Count; i++)
        {
            string param = "decrypt" + i;
            float norm = layersData[i].realKey >= 1f ? Mathf.FloorToInt(layersData[i].realKey) / 100f : layersData[i].realKey;
            var p = new AnimatorControllerParameter { name = param, type = AnimatorControllerParameterType.Float, defaultFloat = norm };
            ac.AddParameter(p);

            var layer = new AnimatorControllerLayer { name = "Layer_" + i, defaultWeight = 1f };
            var sm = new AnimatorStateMachine();
            layer.stateMachine = sm;
            var realState = sm.AddState("DecryptState_" + i);
            realState.motion = layersData[i].realClip;
            sm.defaultState = realState;
            ac.AddLayer(layer);
        }
        AssetDatabase.SaveAssets();
        Animator anim = targetObject.GetComponent<Animator>() ?? targetObject.AddComponent<Animator>();
        anim.runtimeAnimatorController = ac;
    }
}