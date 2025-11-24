using UnityEngine;
using Steamworks;
using System;
using System.Collections;

public class SteamInitializer : MonoBehaviour
{
    public static bool Initialized { get; private set; } = false;
    public static event Action OnSteamInitialized;

    private void Awake()
    {
        var instances = UnityEngine.Object.FindObjectsByType<SteamInitializer>(FindObjectsSortMode.None);

        if (instances.Length > 1)
        {
            Destroy(gameObject);
            return;
        }

        DontDestroyOnLoad(gameObject);
        StartCoroutine(InitSteam());
    }

    private IEnumerator InitSteam()
    {
        yield return null; 

        try
        {
            if (!Packsize.Test() || !DllCheck.Test())
            {
                Debug.LogError("[SteamInitializer] Packsize/DllCheck failed!");
                yield break;
            }

            if (!SteamAPI.Init())
            {
                Debug.LogError("[SteamInitializer] SteamAPI.Init() failed!");
                yield break;
            }

            Initialized = true;
            string name = "Unknown";
            try { name = SteamFriends.GetPersonaName(); } catch { }

            Debug.Log($"[SteamInitializer] Steam initialized as {name}");
            OnSteamInitialized?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogError($"[SteamInitializer] Exception: {e.Message}");
        }
    }

    private void Update()
    {
        if (Initialized)
        {
            try
            {
                SteamAPI.RunCallbacks();
            }
            catch (Exception e)
            {
                Debug.LogError($"[SteamInitializer] Steam callback error: {e.Message}");
            }
        }
    }

    private void OnApplicationQuit()
    {
        StartCoroutine(ShutdownSteamDelayed());
    }

    private IEnumerator ShutdownSteamDelayed()
    {
        yield return null;

        if (Initialized)
        {
            try
            {
                SteamAPI.Shutdown();
                Debug.Log("[SteamInitializer] Steam shutdown successfully.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[SteamInitializer] Steam shutdown error: {e.Message}");
            }
            finally
            {
                Initialized = false;
            }
        }
    }
}
