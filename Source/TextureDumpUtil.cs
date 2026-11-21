using System;
using System.Collections.Generic;
using System.IO;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 开发用：把游戏内已经正确加载的原版贴图导出成 PNG，用于美术参考。
    ///
    /// 为什么需要它：Assets.zip 里的贴图是 BC7 的 .dds.split，
    /// 在外部用 Python/Pillow 解出来颜色是噪声（alpha 对、颜色错），
    /// 但游戏自己加载是正常的。所以让游戏把 Texture2D 反写回 PNG。
    ///
    /// 触发方式（不需要点 UI）：
    ///   在 RimWorld 根目录放一个 _dev\_botania-dev\texture_dump_request.txt，
    ///   每行一个贴图路径（不带 Resources/Textures 前缀与扩展名），
    ///   进游戏即可导出到 _dev\_botania-dev\vanilla_textures\，
    ///   完成后自动把请求文件改名为 texture_dump_done.txt。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class TextureDumpUtil
    {
        private const string RequestFile = @"_dev\_botania-dev\texture_dump_request.txt";
        private const string DoneFile = @"_dev\_botania-dev\texture_dump_done.txt";
        private const string OutDir = @"_dev\_botania-dev\vanilla_textures";

        static TextureDumpUtil()
        {
            try
            {
                DumpOnStartupIfRequested();
            }
            catch (Exception e)
            {
                Log.Error("[Botania] 贴图导出失败：" + e);
            }
        }

        private static string GameRoot()
        {
            try
            {
                // 本模组在 Mods\[GZ]Botania，往上两级就是游戏根目录
                string modRoot = LoadedModManager.GetMod<BotaniaMod>().Content.RootDir;
                return Path.GetFullPath(Path.Combine(modRoot, "..", ".."));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void DumpOnStartupIfRequested()
        {
            string root = GameRoot();
            if (root == null)
            {
                return;
            }
            string request = Path.Combine(root, RequestFile);
            if (!File.Exists(request))
            {
                return;
            }

            string[] paths = File.ReadAllLines(request);
            string outDir = Path.Combine(root, OutDir);
            Directory.CreateDirectory(outDir);

            int ok = 0;
            int fail = 0;
            for (int i = 0; i < paths.Length; i++)
            {
                string texPath = paths[i]?.Trim();
                if (texPath.NullOrEmpty() || texPath.StartsWith("#"))
                {
                    continue;
                }
                try
                {
                    if (SaveTexture(texPath, outDir))
                    {
                        ok++;
                    }
                    else
                    {
                        fail++;
                    }
                }
                catch (Exception)
                {
                    fail++;
                }
            }

            File.Move(request, Path.Combine(root, DoneFile), true);
            Log.Message($"[Botania] 原版贴图导出完成：成功 {ok}，失败 {fail} -> {outDir}");
        }

        private static bool SaveTexture(string texPath, string outDir)
        {
            Texture2D tex = ContentFinder<Texture2D>.Get(texPath, false);
            if (tex == null)
            {
                return false;
            }

            Texture2D readable = tex;
            RenderTexture rt = null;
            Texture2D temp = null;

            if (!tex.isReadable)
            {
                rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(tex, rt);
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                temp = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                temp.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                temp.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                readable = temp;
            }

            byte[] png = readable.EncodeToPNG();
            if (png == null || png.Length == 0)
            {
                return false;
            }

            string safe = texPath.Replace('/', '_').Replace('\\', '_');
            File.WriteAllBytes(Path.Combine(outDir, safe + ".png"), png);
            return true;
        }
    }
}
