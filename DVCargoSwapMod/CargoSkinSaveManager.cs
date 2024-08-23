using DV.JObjectExtstensions;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DVCargoSwapMod
{
    //I was going to make it so that cargo skins were saved.
    //It's a little tricky to do though; as it is, skins would be attached to car GUIDs, and would not
    //change when you changed the cargo (e.g. changing from Iskar to Krugmann containers wouldn't change
    //the 40 ft container skin).
    //For now, I've disabled it. If someone asks for it, we can see what the best course of action would be.

    internal class CargoSkinSaveManager
    {
        private const string SAVE_KEY = "CargoSwapMod_cargoSkins";
        private static readonly Random rand = new();

        private static readonly Dictionary<string, string> carGuidToCargoSkinMap = new Dictionary<string, string>();

        //[HarmonyPatch(typeof(SaveGameManager), nameof(SaveGameManager.Save))]
        class SaveGameManagerPatch
        {
            static void Prefix(SaveGameManager __instance)
            {
                JObject cargoSkinSaveData = GetCargoSkinData();
                SaveGameManager.Instance.data.SetJObject(SAVE_KEY, cargoSkinSaveData);
            }
        }

        //[HarmonyPatch(typeof(CarsSaveManager), nameof(CarsSaveManager.Load))]
        class CarsSaveManagerPatch
        {
            static void Prefix(JObject savedData)
            {
                if (savedData == null)
                {
                    Main.mod.Logger.Error("Save data is null: saved wheel arrangements will not be loaded");
                    return;
                }
                JObject cargoSkinSaveData = SaveGameManager.Instance.data.GetJObject(SAVE_KEY);
                if (cargoSkinSaveData != null)
                {
                    LoadCargoSkinData(cargoSkinSaveData);
                }
            }
        }

        public static JObject GetCargoSkinData()
        {
            JObject cargoSkinSaveData = new JObject();
            JObject[] array = new JObject[carGuidToCargoSkinMap.Count];
            int i = 0;
            foreach (var kvp in carGuidToCargoSkinMap)
            {
                JObject dataObject = new JObject();
                dataObject.SetString("guid", kvp.Key);
                dataObject.SetString("cargoSkin", kvp.Value);
                array[i] = dataObject;
                i++;
            }
            cargoSkinSaveData.SetJObjectArray(CarsSaveManager.CARS_DATA_SAVE_KEY, array);
            return cargoSkinSaveData;
        }

        public static void LoadCargoSkinData(JObject cargoSkinSaveData)
        {
            JObject[] jobjectArray = cargoSkinSaveData.GetJObjectArray(CarsSaveManager.CARS_DATA_SAVE_KEY);
            if (jobjectArray == null)
            {
                return;
            }
            foreach (JObject jobject in jobjectArray)
            {
                string guid = jobject.GetString("guid");
                string cargoSkin = jobject.GetString("cargoSkin");
                if (!carGuidToCargoSkinMap.ContainsKey(guid))
                {
                    carGuidToCargoSkinMap.Add(guid, cargoSkin);
                }
            }
        }

        public static string GetCargoSkin(TrainCar car)
        {
            if (carGuidToCargoSkinMap.ContainsKey(car.CarGUID))
            {
                return carGuidToCargoSkinMap[car.CarGUID];
            }
            return null;
        }

        public static void SetCargoSkin(TrainCar car, string cargoSkin)
        {
            carGuidToCargoSkinMap[car.CarGUID] = cargoSkin;
        }
    }
}
