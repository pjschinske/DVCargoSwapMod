using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Reflection.Emit;
using UnityEngine;
using UnityModManagerNet;
using System.Net;
using DV.Utils;
using DV.ThingTypes;
using DV.ThingTypes.TransitionHelpers;
using System.Xml.Linq;
using DV;
using System.Linq;
using Obi;

namespace DVCargoSwapMod
{

    static class Main
    {
        public static UnityModManager.ModEntry mod;
        // Container prefab names.
        public const string CONTAINER_PREFAB = "C_Flatcar_Container";
        public const string CONTAINER_AC = "AC";
        // C_FlatcarContainerAny
        public const string CONTAINER_ANY = CONTAINER_PREFAB + "Any";
        // C_FlatcarContainerOrange3a2
        public const string CONTAINER_MEDIUM = CONTAINER_PREFAB + "Orange3a2";
        // C_FlatcarContainerSunOmni
        public const string CONTAINER_A1_PREFAB = CONTAINER_PREFAB + "SunOmni";
        // C_FlatcarContainerSunOmniAC
        public const string CONTAINER_A1_AC_PREFAB = CONTAINER_A1_PREFAB + CONTAINER_AC;

        public const string CONTAINER_CARGO_TYPE = "Empty";
        // EmptySunOmni
        public const string CONTAINER_A1_CARGO_TYPE = CONTAINER_CARGO_TYPE + "SunOmni";
        // EmptyOrange3a2
        public const string CONTAINER_MEDIUM_CARGO_TYPE = CONTAINER_PREFAB + "Orange3a2";


        // Sure "Red" and "White" are also brands.
        public static readonly string[] CONTAINER_BRANDS =
        {
            "AAG", "Brohm", "Chemlek", "Goorsk", "Iskar", "Krugmann", "NeoGamma", "Novae", "NovaeOld", "Obco", "Red", "Sperex", "SunOmni", "Traeg", "White"
        };
        public static readonly string[] CONTAINER_AC_BRANDS =
        {
            "Chemlek", "SunOmni", "White"
        };
        public const string DEFAULT_BRAND = "Default";
        public const string SKINS_FOLDER = "Skins";
        public const string SKINS_AC_FOLDER = SKINS_FOLDER + CONTAINER_AC;
        public static readonly string[] TEXTURE_TYPES = new string[] { "_MainTex", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap" };

        public const string CONTAINER_40ft_SHADER = "_MetallicGlossMap";

        // Names of container model prefabs.
        public static HashSet<string> containerPrefabs = new HashSet<string>();
        public static HashSet<string> containerACPrefabs = new HashSet<string>();
        public static StringDictionary containerACPrefabsDict = new StringDictionary();
        // <container brand string, <new brand, is AC>>
        public static Dictionary<string, Dictionary<string, bool>> skinEntries = new Dictionary<string, Dictionary<string, bool>>();
        // <new brand, <texture name, texture>>
        public static Dictionary<string, Dictionary<string, Task<Texture2D>>> skinTextures = new Dictionary<string, Dictionary<string, Task<Texture2D>>>();

        static bool Load(UnityModManager.ModEntry modEntry)
        {
            mod = modEntry;
            Harmony harmony = new Harmony(modEntry.Info.Id);
            // mod.Logger.Log("Made a HarmonyInstance.");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            // mod.Logger.Log("Patch successful.");

            foreach (string brand in CONTAINER_BRANDS)
            {
                containerPrefabs.Add(CONTAINER_PREFAB + brand);
            }

            foreach (string brand in CONTAINER_AC_BRANDS)
            {
                containerACPrefabs.Add(CONTAINER_PREFAB + brand + CONTAINER_AC);
                containerACPrefabsDict.Add(CONTAINER_PREFAB + brand + CONTAINER_AC, CONTAINER_PREFAB + brand);
            }

            LoadSkins(mod.Path + SKINS_FOLDER);
            LoadSkins(mod.Path + SKINS_AC_FOLDER, true);

            foreach (UnityModManager.ModEntry mod in UnityModManager.modEntries)
            {
                //Main.mod.Logger.Log($"Checking if '{mod.Info.DisplayName}' is a cargo skin mod");
                LoadSkinMod(mod);
            }

            return true;
        }

        //Load skins that are installed as other mods
        static void LoadSkinMod(UnityModManager.ModEntry mod)
        {
            //don't want to analyze ourself
            if (mod.Info.Id == Main.mod.Info.Id)
            {
                return;
            }
            
            //only want to analyze mods that depend on CargoSwapMod
            if (!mod.Requirements.ContainsKey(Main.mod.Info.Id))
            {
                return;
            }

            //don't want to load inactive skin mods
            if (!mod.Enabled)
            {
                return;
            }

            Main.mod.Logger.Log($"Found cargo skin mod: '{mod.Info.DisplayName}'");
            //Main.mod.Logger.Log($"Cargo skin mod path: '{mod.Path}'");

            string skinsFolder = mod.Path + SKINS_FOLDER;
            //string skinsACFolder = mod.Path + SKINS_AC_FOLDER;

            if (Directory.Exists(skinsFolder))
            {
                Main.mod.Logger.Log("Found Skins folder for 'mod.Info.DisplayName'");
                LoadSkins(skinsFolder, false);
            }
            /*if (Directory.Exists(skinsACFolder))
            {
                Main.mod.Logger.Log("Found SkinsAC folder");
                LoadSkins(skinsACFolder, true);
            }*/
        }

        /*
         * TODO:
         * - skinEntries probably shouldn't have C_Flatcar_ContainerAny in it
         * - LoadLegacySkins should detect value of isContainerAC based on if skinPrefab ends in AC
         */

        /// <summary>
        /// Damn I need to write a description here.
        /// </summary>
        /// <param name="mainDir"></param>
        /// <param name="containerAC"></param>
        static void LoadSkins(string mainDir, bool containerAC = false)
        {
            if (!Directory.Exists(mainDir))
                return;

            string[] skinPrefabPaths = Directory.GetDirectories(mainDir);
            
            // Traverse folders of skin prefab categories.
            foreach (string skinPrefabPath in skinPrefabPaths)
            {
                
                string skinPrefab = new DirectoryInfo(skinPrefabPath).Name;

                //correct folder name for backwards combatibility with old skins
                skinPrefab = skinPrefab.Replace("C_FlatcarContainer", "C_Flatcar_Container")
                    .Replace("C_FlatcarISOTankYellow2_Asphyxiating", "C_Flatcar_ISOTankYellowAsphyxiating_x2")
                    .Replace("C_FlatcarISOTankYellow2_Asphyxiating", "C_Flatcar_ISOTankYellowAsphyxiating_x2")
                    .Replace("C_FlatcarISOTankYellow2_Explosive", "C_Flatcar_ISOTankYellowExplosive_x2")
                    .Replace("C_FlatcarISOTankYellow2_Oxydizing", "C_Flatcar_ISOTankYellowOxydizing_x2")
                    .Replace("C_FlatcarFarmTractor", "C_Flatcar_FarmTractors");

                bool isSkinAC = skinPrefab.EndsWith(CONTAINER_AC) || containerAC;

                bool allContainers = skinPrefab.Equals(CONTAINER_ANY) && !containerAC;
                bool allContainersAC = skinPrefab.Equals(CONTAINER_ANY + CONTAINER_AC)
                    || (skinPrefab.Equals(CONTAINER_ANY) && containerAC);

                string[] skinBrandPaths = Directory.GetDirectories(skinPrefabPath);

                if (!skinEntries.ContainsKey(skinPrefab))
                    skinEntries[skinPrefab] = new Dictionary<string, bool>();

                // Traverse all folders in skin prefab category.
                foreach (string brandNamePath in skinBrandPaths)
                {
                    string brandName = new DirectoryInfo(brandNamePath).Name;
                    string[] skinFilePaths = Directory.GetFiles(brandNamePath);

                    // Add brand entry to skin prefab list.
                    if (allContainers)
                    {
                        foreach (string containerPrefab in containerPrefabs)
                        {
                            if (!skinEntries.ContainsKey(containerPrefab))
                                skinEntries[containerPrefab] = new Dictionary<string, bool>();
                            // if (!skinEntries[containerPrefab].ContainsKey(brandName))
                            skinEntries[containerPrefab][brandName] = isSkinAC;
                            // skinEntries[containerPrefab][brandName] = containerAC || skinEntries[containerPrefab][brandName];
                        }
                    }
                    else if (allContainersAC)
                    {
                        foreach (string containerACPrefab in containerACPrefabs)
                        {
                            if (!skinEntries.ContainsKey(containerACPrefab))
                                skinEntries[containerACPrefab] = new Dictionary<string, bool>();
                            skinEntries[containerACPrefab][brandName] = isSkinAC;
                        }
                    }
                    else
                    {
                        // if (!skinEntries[skinPrefab].ContainsKey(brandName))
                        skinEntries[skinPrefab][brandName] = isSkinAC;
                        // skinEntries[skinPrefab][brandName] = containerAC || skinEntries[skinPrefab][brandName];
                    }

                    // Don't read any file for default skin.
                    if (!brandName.Equals(DEFAULT_BRAND, StringComparison.OrdinalIgnoreCase))
                    {
                        // For all files in the folder in the skin prefab category.
                        foreach (string skinFilePath in skinFilePaths)
                        {
                            string fileName = Path.GetFileNameWithoutExtension(new FileInfo(skinFilePath).Name);
                            // TODO: Delete line if Altfuture fixes typo in file name.
                            // Update: yes they fixed it (4 years later lol), now we need to correct the other way for backwards compatibility
                            // note the difference between "Altas" and "Atlas"
                            fileName = fileName.Replace("ContainersAltas_01", "ContainersAtlas_01");
                            // there is one other to correct too (again, for backwards compatibility with old skin packs)
                            fileName = fileName.Replace("iso_tank_yellow_d", "ISOTankYellow_01d");

                            // Add texture file for brand entry.
                            if (!skinTextures.ContainsKey(brandName))
                                skinTextures[brandName] = new Dictionary<string, Task<Texture2D>>();
                            // Check if file already read for skin texture.
                            if (!skinTextures[brandName].ContainsKey(fileName))
                            {
                                //Only resize 40ft container textures
                                bool is40ftContainer = skinPrefab.Contains(CONTAINER_PREFAB);
                                // Read file
                                var skinTexture = TextureLoader.Add(new FileInfo(skinFilePath), false, is40ftContainer);
                                skinTextures[brandName][fileName] = skinTexture;
                            }
                        }
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(CargoModelController), "OnCargoLoaded")]
    class CargoModelController_InstantiateCargoModel_Patch
    {

        static private Texture2D ResizedShaderTexture;

        static bool Prefix(CargoModelController __instance)
        {
            /*foreach (var cargo in Globals.G.types.cargos)
            {
                foreach (var loadableCarType in cargo?.loadableCarTypes)
                {
                    foreach (var cargoVariant in loadableCarType?.cargoPrefabVariants)
                    {
                        Main.mod.Logger.Log($"Cargo name: '{cargoVariant.name}'");
                    }
                }
            }*/


            //original onCargoLoaded code
            if (SingletonBehaviour<AudioManager>.Instance.cargoLoadUnload != null && __instance.trainCar.IsCargoLoadedUnloadedByMachine)
            {
                SingletonBehaviour<AudioManager>.Instance.cargoLoadUnload.Play(__instance.trainCar.transform.position, 1f, 1f, 0f, 10f, 500f, default(AudioSourceCurves), null, __instance.trainCar.transform);
            }
            if (__instance.currentCargoModel != null)
            {
                Debug.LogWarning("This shouldn't happen, cargo already instantiated, but new cargo is loaded, deleting currentCargoModel: " + __instance.currentCargoModel.name, __instance);
                __instance.DestroyCurrentCargoModel();
            }
            CargoType_v2 cargoType_v2 = __instance.trainCar.LoadedCargo.ToV2();
            TrainCarType_v2 parentType = __instance.trainCar.carLivery.parentType;
            GameObject[] cargoPrefabsForCarType = cargoType_v2.GetCargoPrefabsForCarType(parentType);

            string skin = null;
            if (cargoPrefabsForCarType == null || cargoPrefabsForCarType.Length == 0)
            {
                return true;
            }
            GameObject original = cargoPrefabsForCarType[UnityEngine.Random.Range(0, cargoPrefabsForCarType.Length)];

        //original prefix

            // Get prefab name.
            string cargoPrefabName = original.name;
            string normalizedCargoPrefab = (string)cargoPrefabName.Clone();

            // Normalize container prefab name.
            bool acswap = Main.containerACPrefabsDict.ContainsKey(cargoPrefabName);
            //if (acswap)
            //    normalizedCargoPrefab = Main.containerACPrefabsDict[cargoPrefabName];

            // Check if there are any skin entries for prefab.
            if (!Main.skinEntries.ContainsKey(normalizedCargoPrefab))
                return true;

            Dictionary<string, bool> skinEntries = Main.skinEntries[normalizedCargoPrefab];
            List<string> skinNames = new List<string>(skinEntries.Keys);

            //choose which skin we want to use
            string savedSkin = null;//CargoSkinSaveManager.GetCargoSkin(__instance.trainCar);
            if (savedSkin is null && skinNames.Count > 0)
            {//skin was not saved for this car, so we randomly pick a new skin
                skin = skinNames[UnityEngine.Random.Range(0, skinNames.Count)];
            }
            else if (skinNames.Contains(savedSkin))
            {
                skin = savedSkin;
            }

            if (skin is not null && !skin.Equals(Main.DEFAULT_BRAND, StringComparison.OrdinalIgnoreCase))
            {
                // We only need to swap out the prefab for normal containers.
                acswap = acswap || skinEntries[skin];
                if (acswap || Main.containerPrefabs.Contains(cargoPrefabName))
                {
                    CargoType_v2 containerCargoTypeV2;
                    bool foundContainerA1Prefab = Globals.G.Types.TryGetCargo(Main.CONTAINER_A1_CARGO_TYPE, out containerCargoTypeV2);
                    if (!foundContainerA1Prefab)
                    {
                        Main.mod.Logger.Error("Could not find the sunomni container prefab");
                        return true;
                    }
                    GameObject[] containerA1Prefabs = containerCargoTypeV2.GetCargoPrefabsForCarType(parentType);
                    /*foreach (GameObject containerA1Prefab in containerA1Prefabs)
                    {
                        Main.mod.Logger.Log($"Container A1 Prefab: '{containerA1Prefab.name}'");
                    }*/

                    //swap in either the sunomni container, or the sunomni ac container; whichever we need
                    original = (acswap && containerA1Prefabs.Length > 1) ? containerA1Prefabs[1] : containerA1Prefabs[0];
                }
            }
            __instance.currentCargoModel = UnityEngine.Object.Instantiate(original, __instance.trainCar.interior.transform, worldPositionStays: false);
            __instance.currentCargoModel.transform.localPosition = Vector3.zero;
            __instance.currentCargoModel.transform.localRotation = Quaternion.identity;
            __instance.trainColliders.SetupCargo(__instance.currentCargoModel);

        //original postfix

            //now that we've spawned in the correct cargo prefab, we need to actually swap the texture
            //for the new texture we chose earlier

            if (skin == null)
            {
                return false;
            }

            bool is40ftContainer = normalizedCargoPrefab.Contains(Main.CONTAINER_PREFAB);

            // Main.mod.Logger.Log(string.Format("Some cargo was loaded into a prefab named {0} on car {1}", __state, ___trainCar.logicCar.ID));
            //GameObject cargoModel = __instance.GetCurrentCargoModel();
            MeshRenderer[] meshes = __instance.currentCargoModel.GetComponentsInChildren<MeshRenderer>();
            foreach (MeshRenderer m in meshes)
            {
                //Main.mod.Logger.Log($"MeshRenderer name: '{m.name}'");
                if (m.material == null)
                    continue;

                if (is40ftContainer)
                {
                    /*m.material.EnableKeyword(Main.CONTAINER_40ft_SHADER.ToUpper());
                    Texture shader = m.material.GetTexture(Main.CONTAINER_40ft_SHADER);*/
                    m.material.mainTextureScale = new Vector2(4, 4);
                }

                foreach (string t in Main.TEXTURE_TYPES)
                {
                    if (!m.material.HasProperty(t))
                    {
                        Main.mod.Logger.Log($"Didn't find '{t}' in '{__instance.currentCargoModel.name}'");
                        continue;
                    }
                    Texture texture = m.material.GetTexture(t);
                    if (texture is Texture2D && Main.skinTextures[skin].ContainsKey(texture.name))
                    {
                        string name = texture.name;
                        Texture2D skinTexture = Main.skinTextures[skin][name].Result;
                        if (!appliedTextures.Contains(skinTexture))
                        {
                            skinTexture.Apply(false, true);
                            appliedTextures.Add(skinTexture);
                        }
                        m.material.SetTexture(t, skinTexture);

                        //Main.mod.Logger.Log($"Loaded texture for {skin}/{name} in {t}.");

                        if (skinTexture.height != skinTexture.width)
                            Main.mod.Logger.Warning($"The texture '{skin}/{name}' is not a square and may render incorrectly.");
                        //dunno why this is here: other texture sizes render just fine. IIRC it just needs to be square
                        /*else if (skinTexture.height != 8192)
                            Main.mod.Logger.Warning($"The texture '{skin}/{name}' is not 8192x8192, but {skinTexture.width}x{skinTexture.height} and may render incorrectly.");*/
                    }
                    else if (texture is Texture2D texture2D && texture.name == "ContainersAtlas_01s")
                    {//if no given shader texture, use default 40ft container shader texture

                        //Default shader texture should be the same for all 40ft containers, so we
                        //cache it to save time (only need to resize it once).
                        if (ResizedShaderTexture is null)
                        {
                            Main.mod.Logger.Log("About to resize texture on 40 ft container");
                            Texture2D readableShader = TextureLoader.DuplicateTexture(texture2D,
                                RenderTextureFormat.BGRA32, RenderTextureReadWrite.Default,
                                texture2D.width, texture2D.height);
                            ResizedShaderTexture = TextureLoader.ResizeTexture(readableShader, TextureFormat.DXT5);
                            ResizedShaderTexture.Apply(true, true);
                            UnityEngine.Object.DontDestroyOnLoad(ResizedShaderTexture);
                            Main.mod.Logger.Log("resized texture on 40 ft container");
                        }
                        m.sharedMaterial.SetTexture(t, ResizedShaderTexture);
                    }
                }
                // m.material.SetTexture("_MainTex", Main.testContainerSkin);
            }

            //  normalizedCargoPrefab
            Main.mod.Logger.Log($"Replaced texture on {__instance.trainCar.ID} with {skin}");

            return false;
        }

        public static readonly HashSet<Texture2D> appliedTextures = new HashSet<Texture2D>();

    }
}
