using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using ElevatorSymphony.Patches;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace ElevatorSymphony
{
    [BepInPlugin(GUID, NAME, VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        internal const string GUID = "com.sk737.elevatorsymphony";
        internal const string NAME = "ElevatorSymphony";
        internal const string VERSION = "1.1.0";

        internal static ManualLogSource LoggerInstance { get; private set; }
        internal static Plugin Instance { get; private set; }
        internal string ExecutingPath { get => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); }

        private string[] mClipFiles;
        internal System.Random Random { get; set; }

        public static new Config Config { get; private set; }
        private Harmony mHarmony = new Harmony(GUID);

        private int mTotalWeight;
        private void Awake()
        {
            Instance = this;
            LoggerInstance = Logger;

            if (Directory.Exists(ExecutingPath + "/Music")) {
                mClipFiles = [.. Directory.GetFiles(ExecutingPath + "/Music").OrderBy(f => f)];
            }
            Config = new Config(base.Config, mClipFiles);
            foreach (ConfigEntry<int> e in Config.SongWeights) {
                mTotalWeight += e.Value;
            }
            
            mHarmony.PatchAll(typeof(RoundManagerPatch));
            mHarmony.PatchAll(typeof(StartStartOfRoundPatch));
            mHarmony.PatchAll(typeof(Plugin));
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
        }

        public AudioType GetAudioType(string extension) {
            switch (extension) {
                default:
                case ".ogg":
                    return AudioType.OGGVORBIS;
                case ".mp3":
                    return AudioType.MPEG;
                case ".wav":
                    return AudioType.WAV;
            }
        }




        public async Task LoadClip(string path, Action<AudioClip> callback) {
            AudioType type = GetAudioType(Path.GetExtension(path));
            UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(path, type);
            try {
                request.SendWebRequest();
                while (!request.isDone) {
                    await Task.Delay(50);
                }
                if (request.result != UnityWebRequest.Result.Success) {
                    Logger.LogError($"Failed to load file:{path}, + {request.error}");
                }
                else {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
                    Logger.LogMessage($"Loaded file: {path}");
                    callback.Invoke(clip);
                }
            }
            finally {
                request?.Dispose();
            }
        }

        public void LoadNewAudioFile() {
            //int selected = Random.Next(Config.IncludeDefaultElevatorMusic.Value ? -1 : 0, mClipFiles.Length);

            int selectedWeight = Random.Next(0, mTotalWeight);
            int selectedSong = -1;
            int prevMax = 0;

            for (int i = 0; i < Config.SongWeights.Count; i++) {
                int min = prevMax;
                prevMax += Config.SongWeights[i].Value;

                if (selectedWeight > min && selectedWeight < prevMax) {
                    selectedSong = (i-1);
                    break;
                }
            }



            if (selectedSong >= 0 && mClipFiles.Length > 0) {
                LoadClip(mClipFiles[selectedSong], SetElevator);
            }
        }

        public void SetElevator(AudioClip clip) {
            MineshaftElevatorController elevator = FindObjectOfType<MineshaftElevatorController>();
            if (elevator != null) {
                elevator.elevatorJingleMusic.clip = clip;
            }
        }
    }

    public class Config {
        public static List<ConfigEntry<int>> SongWeights = [];
        public Config(ConfigFile config, string[] clipFiles) {
            SongWeights.Add(
                config.Bind(
                    "General",
                    "Default chance",
                    100,
                    "Chance of the base games song being picked by the randomizer. The higher the value the greater the chance it is picked."));
            foreach (string c in clipFiles) {
                string name = Path.GetFileNameWithoutExtension(c);
                SongWeights.Add(config.Bind(
                    "General",
                    $"{name} chance",
                    100,
                    $"Chance of {name} being picked by the randomizer. The higher the value the greater the chance it is picked."
                    ));
            }
        }
    }
}

namespace ElevatorSymphony.Patches {
    [HarmonyPatch(typeof(RoundManager))]
    internal class RoundManagerPatch {
        [HarmonyPatch("InitializeRandomNumberGenerators")]
        [HarmonyPrefix]
        public static void SeedPatch(ref RoundManager __instance) {
            Plugin.LoggerInstance.LogInfo($"Initializing random with seed {__instance.playersManager.randomMapSeed}");
            Plugin.Instance.Random = new System.Random(__instance.playersManager.randomMapSeed);
        }
    }

    [HarmonyPatch(typeof(StartOfRound))]
    internal class StartStartOfRoundPatch {
        [HarmonyPatch("openingDoorsSequence")]
        [HarmonyPostfix]
        public static void openingDoorsSequencePatch() {
            Plugin.Instance.LoadNewAudioFile();
        }
    }


}
