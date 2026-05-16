using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

public class AudioManager : MonoBehaviour
{
    private static bool isInitialized = false;

    /// <summary> 为 true 时使用 MainAudioMixer 与各 <see cref="AudioGroup"/>；为 false 时 AudioSource 不挂任何 Mixer（全部分组与音量滑轨不生效）。 </summary>
    public static bool UseMixerRouting { get; private set; }

    [Tooltip("勾选：按 Music / SFX / UI 走 AudioMixer；不勾选：不走任何 Mixer 逻辑")]
    [SerializeField] bool useMixerRouting = false;

    [Header("Mixer")]
    private static AudioMixer mainMixer;

    [Header("Audio Mixer Groups")]
    private static AudioMixerGroup masterGroup; // for all
    private static AudioMixerGroup musicGroup;
    private static AudioMixerGroup sfxGroup;
    private static AudioMixerGroup uiGroup;

    public enum AudioGroup
    {
        Music,
        SFX,
        UI
    }

    private static Dictionary<string, AudioClip> audioClipDict = new Dictionary<string, AudioClip>();
    private static Dictionary<string, AudioSource> audioSourceDict = new Dictionary<string, AudioSource>();
    private static GameObject audioParent;

    static void ClearMixerGroups()
    {
        mainMixer = null;
        masterGroup = null;
        musicGroup = null;
        sfxGroup = null;
        uiGroup = null;
    }

    private static void Initialize()
    {
        if (isInitialized) return;

        var am = Object.FindObjectOfType<AudioManager>();
        if (am != null)
            UseMixerRouting = am.useMixerRouting;

        if (UseMixerRouting)
        {
            mainMixer = Resources.Load<AudioMixer>("MainAudioMixer");
            if (mainMixer == null)
            {
                Debug.LogWarning("[AudioManager] MainAudioMixer 未找到（Resources/MainAudioMixer），将关闭 Mixer 路由。");
                UseMixerRouting = false;
                ClearMixerGroups();
            }
            else
            {
                musicGroup = mainMixer.FindMatchingGroups("Music")[0];
                sfxGroup = mainMixer.FindMatchingGroups("SFX")[0];
                uiGroup = mainMixer.FindMatchingGroups("UI")[0];
                masterGroup = mainMixer.FindMatchingGroups("Master")[0];
            }
        }
        else
            ClearMixerGroups();

        SceneManager.sceneLoaded += (scene, mode) => audioSourceDict.Clear();

        isInitialized = true;
    }

    static void ApplyMixerGroupIfRouting(AudioSource audioSource, AudioMixerGroup group)
    {
        if (audioSource == null)
            return;
        audioSource.outputAudioMixerGroup = UseMixerRouting ? group : null;
    }

    #region PubicFunctions
    /// <summary>
    /// 预加载一个 clip 进缓存，避免首次 Play / PlayOneShot 时同步读盘造成的 hitch。
    /// 调用多次同一 clip 是安全的（dict 命中直接返回）。
    /// </summary>
    public static void Preload(string clipName)
    {
        Initialize();
        if (string.IsNullOrEmpty(clipName)) return;
        if (audioClipDict.ContainsKey(clipName)) return;

        AudioClip clip = Resources.Load<AudioClip>(clipName);
        if (clip == null)
        {
            Debug.LogWarning($"[AudioManager] Preload failed, clip not found: {clipName}");
            return;
        }
        audioClipDict.Add(clipName, clip);

        // 给压缩+解压在播放时进行的 clip 一个先解压的机会
        if (clip.loadState == AudioDataLoadState.Unloaded)
            clip.LoadAudioData();
    }

    /// <summary>
    /// Play a clip, with default master AudioGroup or with previous used AudioGroup
    /// </summary>
    /// <param name="clipName"></param>
    public static void Play(string clipName)
    {
        Initialize(); //make sure AudioManager is initialized

        //check if there is a audio source object
        AudioSource audioSource;

        if (!audioSourceDict.ContainsKey(clipName))
        {
            audioSource = new GameObject("Audio_" +clipName).AddComponent<AudioSource>();
            audioSourceDict.Add(clipName, audioSource);
            ApplyMixerGroupIfRouting(audioSource, masterGroup);
        }
        audioSource = audioSourceDict[clipName];

        //create a audioParent if hasn't had one
        if (audioParent == null)
        {
            audioParent = new GameObject("Default AudioParent");
        }
        audioSource.transform.parent = audioParent.transform;

        //check if clip has been loaded
        AudioClip clip;

        if (!audioClipDict.ContainsKey(clipName))
        {
            clip = Resources.Load<AudioClip>(clipName);
            if (clip == null)
            {
                Debug.LogError("Clip <" + clipName + "> cannot be found in Resources folder");
            }
            audioClipDict.Add(clipName, clip);
        }
        clip = audioClipDict[clipName];

        audioSource.clip = clip;
        //audioSource.outputAudioMixerGroup = mainMixer.outputAudioMixerGroup;
        audioSource.Play();
    }

    /// <summary>
    /// Play a clip under a parent GameObject, with default master AudioGroup or with previous used AudioGroup
    /// </summary>
    /// <param name="clipName"></param>
    /// <param name="parent"></param>
    public static void Play(string clipName, Transform parent)
    {
        Play(clipName);
        GetAudioSource(clipName).transform.parent = parent;
    }

    /// <summary>
    /// Play a clip, with specific AudioGroup
    /// </summary>
    /// <param name="clipName"></param>
    /// <param name="audioGroup"></param>
    public static void Play(string clipName, AudioGroup audioGroup)
    {
        Initialize();
        if (!UseMixerRouting)
        {
            Play(clipName);
            return;
        }

        //Get targe AudioMixerGroup
        AudioMixerGroup targeAudioGroup = audioGroup switch
        {
            AudioGroup.Music => musicGroup,
            AudioGroup.SFX => sfxGroup,
            AudioGroup.UI => uiGroup,
            _ => masterGroup
        };

        Play(clipName);
        ApplyMixerGroupIfRouting(GetAudioSource(clipName), targeAudioGroup);
    }

    /// <summary>
    /// Play a clip under a parent GameObject, with specific AudioGroup
    /// </summary>
    /// <param name="clipName"></param>
    /// <param name="audioGroup"></param>
    /// <param name="parent"></param>
    public static void Play(string clipName, AudioGroup audioGroup, Transform parent)
    {
        Play(clipName, audioGroup);
        GetAudioSource(clipName).transform.parent = parent;
    }

    /// <summary>
    /// Play a clip for one time, with default master AudioGroup or with previous used AudioGroup
    /// </summary>
    /// <param name="clipName"></param>
    public static void PlayOneShot(string clipName)
    {
        Initialize(); //make sure AudioManager is initialized

        //check if there is a audio source object
        AudioSource audioSource;

        if (!audioSourceDict.ContainsKey(clipName))
        {
            audioSource = new GameObject("Audio_" + clipName).AddComponent<AudioSource>();
            audioSourceDict.Add(clipName, audioSource);
            ApplyMixerGroupIfRouting(audioSource, masterGroup);
        }
        audioSource = audioSourceDict[clipName];

        //create a audioParent if hasn't had one
        if (audioParent == null)
        {
            audioParent = new GameObject("Default AudioParent");
        }
        audioSource.transform.parent = audioParent.transform;

        //check if clip has been loaded
        AudioClip clip;

        if (!audioClipDict.ContainsKey(clipName))
        {
            clip = Resources.Load<AudioClip>(clipName);
            if (clip == null)
            {
                Debug.LogError("Clip <" + clipName + "> cannot be found in Resources folder");
            }
            audioClipDict.Add(clipName, clip);
        }
        clip = audioClipDict[clipName];

        audioSource.clip = clip;
        //audioSource.outputAudioMixerGroup = mainMixer.outputAudioMixerGroup;
        audioSource.PlayOneShot(clip);
    }

    /// <summary>
    /// Play a clip for one time, with specific AudioGroup
    /// </summary>
    /// <param name="clipName"></param>
    /// <param name="audioGroup"></param>
    public static void PlayOneShot(string clipName, AudioGroup audioGroup)
    {
        Initialize();
        if (!UseMixerRouting)
        {
            PlayOneShot(clipName);
            return;
        }

        //Get targe AudioMixerGroup
        AudioMixerGroup targeAudioGroup = audioGroup switch
        {
            AudioGroup.Music => musicGroup,
            AudioGroup.SFX => sfxGroup,
            AudioGroup.UI => uiGroup,
            _ => masterGroup
        };

        PlayOneShot(clipName);
        ApplyMixerGroupIfRouting(GetAudioSource(clipName), targeAudioGroup);
    }

    /// <summary>
    /// Play a clip for one time under a parent GameObject, with default master AudioGroup or with previous used AudioGroup
    /// </summary>
    /// <param name="clipName"></param>
    /// <param name="parent"></param>
    public static void PlayOneShot(string clipName, Transform parent)
    {
        PlayOneShot(clipName);
        GetAudioSource(clipName).transform.parent = parent;
    }

    /// <summary>
    /// Play a clip for one time under a parent GameObject, with specific AudioGroup
    /// </summary>
    /// <param name="clipName"></param>
    /// <param name="audioGroup"></param>
    public static void PlayOneShot(string clipName, AudioGroup audioGroup, Transform parent)
    {
        PlayOneShot(clipName, audioGroup);
        GetAudioSource(clipName).transform.parent = parent;
    }

    /// <summary>
    /// play music, assign AudioGroup as music
    /// </summary>
    /// <param name="clipName"></param>
    /// <param name="loop"></param>
    public static void PlayMusic(string clipName, bool loop)
    {
        Initialize(); //make sure AudioManager is initialized

        //check if there is a audio source object
        AudioSource audioSource;

        if (!audioSourceDict.ContainsKey(clipName))
        {
            audioSource = new GameObject("Audio_" + clipName).AddComponent<AudioSource>();
            audioSourceDict.Add(clipName, audioSource);
        }
        audioSource = audioSourceDict[clipName];
        ApplyMixerGroupIfRouting(audioSource, musicGroup);
        audioSource.loop = loop;

        //create a audioParent if hasn't had one
        if (audioParent == null)
        {
            audioParent = new GameObject("Default AudioParent");
        }
        audioSource.transform.parent = audioParent.transform;

        //check if clip has been loaded
        AudioClip clip;

        if (!audioClipDict.ContainsKey(clipName))
        {
            clip = Resources.Load<AudioClip>(clipName);
            if (clip == null)
            {
                Debug.LogError("Clip <" + clipName + "> cannot be found in Resources folder");
            }
            audioClipDict.Add(clipName, clip);
        }
        clip = audioClipDict[clipName];

        audioSource.clip = clip;
        //audioSource.outputAudioMixerGroup = mainMixer.outputAudioMixerGroup;
        audioSource.Play();
    }

    /// <summary>
    /// play music under a parent GameObject, assign AudioGroup as music
    /// </summary>
    /// <param name="clipName"></param>
    /// <param name="loop"></param>
    /// <param name="parent"></param>
    public static void PlayMusic(string clipName, bool loop, Transform parent)
    {
        PlayMusic(clipName, loop);
        GetAudioSource(clipName).transform.parent = parent;
    }

    public static void Stop(string clipName)
    {
        if (ClipExist(clipName))
        {
            AudioSource audioSource = audioSourceDict[clipName];
            audioSource.Stop();
        }
    }

    /// <summary>
    /// Set Volume for one clip
    /// </summary>
    /// <param name="clipName"></param>
    /// <param name="volume"></param>
    public static void SetVolume(string clipName, float volume)
    {
        if (ClipExist(clipName))
        {
            AudioSource audioSource = audioSourceDict[clipName];
            audioSource.volume = volume;
        }
    }

    // change AudioGroup volume
    public static void SetMasterGroupVolume(float volume)
    {
        Initialize();
        if (!UseMixerRouting || mainMixer == null)
            return;
        // 如果slider是0-100，先转换成0-1
        float normalizedVolume = volume / 100f;
        float dbValue = normalizedVolume > 0.0001f ? Mathf.Log10(normalizedVolume) * 20 : -80f;
        mainMixer.SetFloat("MasterVolume", dbValue);
    }

    public static void SetMusicGroupVolume(float volume)
    {
        Initialize();
        if (!UseMixerRouting || mainMixer == null)
            return;
        // 如果slider是0-100，先转换成0-1
        float normalizedVolume = volume / 100f;
        float dbValue = normalizedVolume > 0.0001f ? Mathf.Log10(normalizedVolume) * 20 : -80f;
        mainMixer.SetFloat("MusicVolume", dbValue);
    }

    public static void SetSFXGroupVolume(float volume)
    {
        Initialize();
        if (!UseMixerRouting || mainMixer == null)
            return;
        // 如果slider是0-100，先转换成0-1
        float normalizedVolume = volume / 100f;
        float dbValue = normalizedVolume > 0.0001f ? Mathf.Log10(normalizedVolume) * 20 : -80f;
        mainMixer.SetFloat("SFXVolume", dbValue);
    }

    public static AudioSource GetAudioSource(string clipName)
    {
        if (ClipExist(clipName))
        {
            return audioSourceDict[clipName];
        } else
        {
            return null;
        }
    }
    #endregion

    #region PrivateFunctions
    private static bool ClipExist(string clipName)
    {
        //check if there is a audio source object
        AudioSource audioSource;

        if (!audioSourceDict.ContainsKey(clipName))
        {
            Debug.LogError("AudioSource GameObject <Audio_" + clipName + "> cannot be found in current scene");
            return false;
        }
        audioSource = audioSourceDict[clipName];

        //check if clip has been loaded
        if (!audioClipDict.ContainsKey(clipName))
        {
            Debug.LogError("AudioClip <" + clipName + "> has not been loaded.");
            return false;
        }

        string clipFileName = System.IO.Path.GetFileName(clipName);
        if (audioSource.clip.name != clipFileName)
        {
            Debug.LogError("AudioClip <" + clipName + "> cannot be found in AudioSource GameObject < Audio_" + clipName + " >");
            return false;
        }

        return true;
    }
    #endregion

    #region OldAudioManager(Don't Use!!!!!!!!!!)

    void Start()
    {
        // 启动时自动分配所有现有的AudioSource
        AssignAllAudioSources();
    }
    // 在场景加载后调用
    void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        // 场景加载后自动分配
        AssignAllAudioSources();
    }

    // 自动给所有AudioSource分配MixerGroup
    public void AssignAllAudioSources()
    {
        Initialize();
        if (!UseMixerRouting || mainMixer == null)
            return;

        AudioSource[] allAudioSources = FindObjectsOfType<AudioSource>(true); // 包括未激活的

        foreach (AudioSource audioSource in allAudioSources)
        {
            // 如果已经有MixerGroup就跳过
            if (audioSource.outputAudioMixerGroup != null) continue;

            // 根据tag或名字自动分配（你可以自定义规则）
            if (audioSource.CompareTag("Audio_Music"))
            {
                audioSource.outputAudioMixerGroup = musicGroup;
            }
            else if (audioSource.CompareTag("Audio_UI"))
            {
                audioSource.outputAudioMixerGroup = uiGroup;
            }
            else if (audioSource.CompareTag("Audio_SFX"))
            {
                audioSource.outputAudioMixerGroup = sfxGroup;
            } 
            else
            {
                audioSource.outputAudioMixerGroup = mainMixer.outputAudioMixerGroup; // 默认
            }
        }
    }



    #endregion
}