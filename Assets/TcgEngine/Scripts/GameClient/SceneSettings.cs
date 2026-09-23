using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TcgEngine.Client
{
    /// <summary>
    /// Component added to a scene to add some generic sfx/music to the arena
    /// </summary>

    public class SceneSettings : MonoBehaviour
    {
        public AudioClip start_audio;
        public AudioClip[] game_music;
        public AudioClip[] game_ambience;

        private static SceneSettings instance;

        private void Awake()
        {
            instance = this;
        }

        void Start()
        {
            AudioTool.Get().PlaySFX("game_sfx", start_audio);
            //对战 BGM：配置表 battle 条目优先；未配置则回退本场景随机游戏音乐（game_music，迁移兼容）
            AudioClip fallback = (game_music != null && game_music.Length > 0)
                ? game_music[Random.Range(0, game_music.Length)]
                : null;
            TcgEngine.Audio.BgmManager.PlayFor(TcgEngine.Audio.BgmKeys.Battle, false, fallback, 0.35f);
            if (game_ambience.Length > 0)
                AudioTool.Get().PlaySFX("ambience", game_ambience[Random.Range(0, game_ambience.Length)], 0.5f, true);
        }

        public static SceneSettings Get()
        {
            return instance;
        }
    }
}
