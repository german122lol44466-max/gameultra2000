using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace SW.Commander.Net
{
    /// <summary>
    /// Инициализация Steamworks (живёт всю сессию). Без Steam игра работает: одиночная игра и игра по IP.
    /// App ID — из steam_appid.txt рядом с .exe (480 — тестовый Spacewar, пока нет своего).
    /// </summary>
    public class SteamBootstrap : MonoBehaviour
    {
        public static SteamBootstrap Instance { get; private set; }
        public static bool Initialized { get; private set; }
        public static string Error { get; private set; }
        public static event Action Ready;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Create()
        {
            if (Instance != null) return;
            var go = new GameObject("[Steam]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<SteamBootstrap>();
        }

        void Awake()
        {
            try
            {
                if (!Packsize.Test()) { Error = "Steamworks.NET: неверная платформа"; Debug.LogError(Error); return; }
                if (!DllCheck.Test()) { Error = "Steamworks.NET: неверная версия steam_api"; Debug.LogError(Error); return; }
                var r = SteamAPI.InitEx(out var message);
                Initialized = r == ESteamAPIInitResult.k_ESteamAPIInitResult_OK;
                if (!Initialized)
                {
                    Error = r == ESteamAPIInitResult.k_ESteamAPIInitResult_NoSteamClient ? "Steam не запущен" : "Steam недоступен: " + message;
                    Debug.LogWarning(Error);
                    return;
                }
            }
            catch (DllNotFoundException e)
            {
                Error = "Не найдена библиотека steam_api";
                Debug.LogError(Error + "\n" + e);
                return;
            }
            Debug.Log($"Steam: {SteamFriends.GetPersonaName()} ({SteamUser.GetSteamID()})");
            Ready?.Invoke();
        }

        void Update()
        {
            if (Initialized) SteamAPI.RunCallbacks();
        }

        void OnApplicationQuit()
        {
            if (!Initialized) return;
            Initialized = false;
            SteamAPI.Shutdown();
        }

        public static string LocalName => Initialized ? SteamFriends.GetPersonaName() : Environment.UserName;
        public static ulong LocalId => Initialized ? SteamUser.GetSteamID().m_SteamID : 0;

        // ------------------------------------------------------------------ аватарки
        static readonly Dictionary<ulong, Texture2D> avatars = new Dictionary<ulong, Texture2D>();

        /// <summary>Аватар Steam (кэш). Может вернуть null, пока Steam не скачал картинку — спросите позже.</summary>
        public static Texture2D Avatar(ulong steamId)
        {
            if (!Initialized || steamId == 0) return null;
            if (avatars.TryGetValue(steamId, out var t) && t) return t;
            int h = SteamFriends.GetMediumFriendAvatar(new CSteamID(steamId));
            if (h <= 0) { SteamFriends.RequestUserInformation(new CSteamID(steamId), false); return null; }
            if (!SteamUtils.GetImageSize(h, out uint w, out uint hh) || w == 0) return null;
            var data = new byte[w * hh * 4];
            if (!SteamUtils.GetImageRGBA(h, data, data.Length)) return null;
            // Steam отдаёт строки сверху вниз — переворачиваем для Unity
            var flipped = new byte[data.Length];
            int row = (int)w * 4;
            for (int y = 0; y < hh; y++) Buffer.BlockCopy(data, y * row, flipped, ((int)hh - 1 - y) * row, row);
            t = new Texture2D((int)w, (int)hh, TextureFormat.RGBA32, false);
            t.LoadRawTextureData(flipped);
            t.Apply();
            avatars[steamId] = t;
            return t;
        }
    }
}
