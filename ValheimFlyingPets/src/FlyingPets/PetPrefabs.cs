using System;
using System.Collections.Generic;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlyingPets
{
    /// <summary>Builds one network prefab per loaded pet: rigidbody, colliders, skinned mesh, bones, saddle anchor.</summary>
    internal static class PetPrefabs
    {
        private static GameObject s_holder;

        public static void BuildAll()
        {
            if (PetLibrary.Pets.Count == 0)
            {
                return;
            }

            ReadVanillaSettings(out var interpolation);
            var template = FindCreatureMaterial();
            foreach (var asset in PetLibrary.Pets)
            {
                if (asset.Prefab != null)
                {
                    continue;
                }

                try
                {
                    asset.Prefab = Build(asset, template, interpolation);
                    PrefabManager.Instance.AddPrefab(asset.Prefab);
                    FlyingPetsPlugin.Log.LogInfo("Registered " + asset.PrefabName);
                }
                catch (Exception e)
                {
                    FlyingPetsPlugin.Log.LogError("Could not build " + asset.PrefabName + ": " + e);
                }
            }
        }

        /// <summary>Takes the riding pose from the vanilla mounts and the physics smoothing from the player.</summary>
        private static void ReadVanillaSettings(out RigidbodyInterpolation interpolation)
        {
            interpolation = RigidbodyInterpolation.Interpolate;
            foreach (string mount in new[] { "Asksvin", "Lox" })
            {
                var prefab = PrefabManager.Instance.GetPrefab(mount);
                var saddle = prefab != null ? prefab.GetComponentInChildren<Sadle>(true) : null;
                if (saddle != null && !string.IsNullOrEmpty(saddle.m_attachAnimation))
                {
                    FlyingPet.RideAnimation = saddle.m_attachAnimation;
                    break;
                }
            }

            var player = PrefabManager.Instance.GetPrefab("Player");
            var body = player != null ? player.GetComponent<Rigidbody>() : null;
            if (body != null)
            {
                // the rider is moved with the pet; both must be smoothed the same way or the rider jitters
                interpolation = body.interpolation;
            }

            FlyingPetsPlugin.Log.LogInfo("Riding pose: " + FlyingPet.RideAnimation + ", smoothing: " + interpolation);
        }

        private static Material FindCreatureMaterial()
        {
            foreach (string creature in new[] { "Deer", "Boar", "Lox", "Wolf", "Neck" })
            {
                var prefab = PrefabManager.Instance.GetPrefab(creature);
                if (prefab == null)
                {
                    continue;
                }

                foreach (var smr in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    foreach (var mat in smr.sharedMaterials)
                    {
                        if (mat != null && mat.shader != null && mat.shader.name.IndexOf("Creature", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return mat;
                        }
                    }
                }
            }

            return null;
        }

        private static Material BodyMaterial(PetAsset asset, Material template)
        {
            Material mat;
            if (template != null)
            {
                mat = new Material(template);
                foreach (string prop in mat.GetTexturePropertyNames())
                {
                    if (prop == "_MainTex" || prop == "_BumpMap")
                    {
                        continue;
                    }

                    string p = prop.ToLowerInvariant();
                    if (p.Contains("emission") || p.Contains("metal") || p.Contains("gloss") || p.Contains("spec") ||
                        p.Contains("occlusion") || p.Contains("detail") || p.Contains("rough") || p.Contains("mask") ||
                        p.Contains("height") || p.Contains("parallax"))
                    {
                        mat.SetTexture(prop, null);
                    }
                }
            }
            else
            {
                var shader = Shader.Find("Custom/Creature") ?? Shader.Find("Standard");
                mat = new Material(shader);
            }

            mat.name = "FP_" + asset.Key;
            mat.SetTexture("_MainTex", asset.Albedo);
            mat.SetTextureScale("_MainTex", Vector2.one);
            mat.SetTextureOffset("_MainTex", Vector2.zero);
            if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", Color.white);
            }

            if (asset.Normal != null && mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", asset.Normal);
                mat.SetTextureScale("_BumpMap", Vector2.one);
                mat.SetTextureOffset("_BumpMap", Vector2.zero);
                mat.EnableKeyword("_NORMALMAP");
            }

            if (mat.HasProperty("_EmissionColor"))
            {
                mat.SetColor("_EmissionColor", Color.black);
            }

            if (mat.HasProperty("_Metallic"))
            {
                mat.SetFloat("_Metallic", 0f);
            }

            if (mat.HasProperty("_Glossiness"))
            {
                mat.SetFloat("_Glossiness", 0.22f);
            }

            return mat;
        }

        private static Material EyeMaterial(PetAsset asset, Material body)
        {
            Color glow = GlowColor(asset);
            var unlit = Shader.Find("Sprites/Default");
            Material mat;
            if (unlit != null)
            {
                mat = new Material(unlit);
                mat.color = glow;
            }
            else
            {
                mat = new Material(body);
                mat.SetTexture("_MainTex", Texture2D.whiteTexture);
                if (mat.HasProperty("_Color"))
                {
                    mat.SetColor("_Color", glow);
                }

                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", glow * 2.5f);
                }
            }

            mat.name = "FP_" + asset.Key + "_eyes";
            return mat;
        }

        private static Color GlowColor(PetAsset asset)
        {
            var g = asset.Data.eyeGlow;
            return g != null && g.Length >= 3 ? new Color(g[0], g[1], g[2], 1f) : new Color(0.42f, 0.93f, 1f, 1f);
        }

        private static GameObject Holder()
        {
            if (s_holder == null)
            {
                s_holder = new GameObject("FlyingPets_Prefabs");
                UnityEngine.Object.DontDestroyOnLoad(s_holder);
                s_holder.SetActive(false); // nothing inside wakes up until it is instantiated
            }

            return s_holder;
        }

        private static GameObject Build(PetAsset asset, Material template, RigidbodyInterpolation interpolation)
        {
            var go = new GameObject(asset.PrefabName);
            go.transform.SetParent(Holder().transform, false);
            int layer = LayerMask.NameToLayer("character");
            if (layer >= 0)
            {
                go.layer = layer;
            }

            var nview = go.AddComponent<ZNetView>();
            nview.m_persistent = false; // a summon: gone when its owner leaves, re-summoned with the staff
            nview.m_type = ZDO.ObjectType.Prioritized;
            nview.m_distant = false;

            var body = go.AddComponent<Rigidbody>();
            body.mass = 400f;
            body.useGravity = false;
            body.isKinematic = false;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.interpolation = interpolation;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.linearDamping = 0f;
            body.angularDamping = 0f;

            var sync = go.AddComponent<ZSyncTransform>();
            sync.m_syncPosition = true;
            sync.m_syncRotation = true;
            sync.m_syncScale = false;
            sync.m_syncBodyVelocity = false;

            // the torso only: legs and wings never snag on anything
            var data = asset.Data;
            int bodyIndex = Math.Max(0, asset.Bone("body"));
            Vector3 bodyPivot = asset.Pivots[bodyIndex];
            float radius = data.bodyRadius > 0.05f ? data.bodyRadius + 0.06f : 0.48f;
            Vector3 lo = PetAsset.V3(data.boundsMin, new Vector3(-1f, 0f, -1.2f));
            Vector3 hi = PetAsset.V3(data.boundsMax, new Vector3(1f, 2.5f, 1.2f));
            float length = Mathf.Clamp((hi.z - lo.z) * 0.62f, radius * 2.2f, 3.2f);
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.direction = 2;
            capsule.radius = radius;
            capsule.height = length;
            capsule.center = new Vector3(0f, bodyPivot.y, bodyPivot.z * 0.5f);

            // visuals: Visual (scaled) > Mesh + bone hierarchy
            var visual = new GameObject("Visual").transform;
            visual.SetParent(go.transform, false);
            var meshGo = new GameObject("Mesh");
            meshGo.transform.SetParent(visual, false);

            int nb = asset.BoneNames.Length;
            var bones = new Transform[nb];
            for (int i = 0; i < nb; i++)
            {
                var t = new GameObject(asset.BoneNames[i]).transform;
                int parent = asset.BoneParents[i];
                t.SetParent(parent >= 0 ? bones[parent] : visual, false);
                t.localPosition = parent >= 0 ? asset.Pivots[i] - asset.Pivots[parent] : asset.Pivots[i];
                t.localRotation = Quaternion.identity;
                bones[i] = t;
            }

            var smr = meshGo.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = asset.Mesh;
            smr.bones = bones;
            smr.rootBone = bones[bodyIndex];
            smr.quality = SkinQuality.Bone4;
            smr.updateWhenOffscreen = false;
            smr.localBounds = new Bounds(Vector3.zero, new Vector3(9f, 7f, 9f));
            smr.shadowCastingMode = ShadowCastingMode.On;
            smr.receiveShadows = true;
            var bodyMat = BodyMaterial(asset, template);
            var mats = new List<Material> { bodyMat };
            for (int s = 1; s < asset.Mesh.subMeshCount; s++)
            {
                mats.Add(EyeMaterial(asset, bodyMat));
            }

            smr.sharedMaterials = mats.ToArray();

            // saddle anchor: the rider sits here (its parent bone carries the body's bob and tilt)
            var seat = new GameObject("FP_Seat").transform;
            seat.SetParent(bones[bodyIndex], false);
            seat.localPosition = PetAsset.V3(data.seat, bodyPivot + Vector3.up * 0.4f) - bodyPivot;

            // faint glow around the eyes
            Light eyeLight = null;
            int headIndex = asset.Bone(string.IsNullOrEmpty(data.headBone) ? "head" : data.headBone);
            if (asset.HasEyes && headIndex >= 0)
            {
                var lightGo = new GameObject("FP_EyeLight");
                lightGo.transform.SetParent(bones[headIndex], false);
                lightGo.transform.localPosition = asset.EyeCenter - asset.Pivots[headIndex] + Vector3.forward * 0.12f;
                eyeLight = lightGo.AddComponent<Light>();
                eyeLight.type = LightType.Point;
                eyeLight.color = GlowColor(asset);
                eyeLight.intensity = 1.1f;
                eyeLight.range = 1.6f;
                eyeLight.shadows = LightShadows.None;
            }

            var pet = go.AddComponent<FlyingPet>();
            pet.m_petKey = asset.Key;
            pet.m_visual = visual;
            pet.m_bones = bones;
            pet.m_seat = seat;
            pet.m_eyeLight = eyeLight;
            pet.m_capsuleCenter = capsule.center;
            pet.m_capsuleRadius = capsule.radius;
            pet.m_capsuleHeight = capsule.height;
            pet.m_eyeLightRange = 1.6f;
            return go;
        }
    }
}
