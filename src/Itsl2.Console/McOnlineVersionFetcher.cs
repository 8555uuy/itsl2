using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;

// 【在线版本拉取器：联网拉 Mojang 官方清单，只留正式版 release，并解析版本详情】
public class McOnlineVersionFetcher
{
    // 官方版本清单地址（只在这里维护，不在别处抄）
    private const string 清单地址 = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    // 全类共用一个 HttpClient（原来每个方法各 new 一个，白瞎连接池）
    private static readonly HttpClient 客户端 = new HttpClient();

    // 方法：拉全部版本清单，只留 type=release 的正式版
    public List<OnlineVersionInfo> FetchReleaseVersions()
    {
        List<OnlineVersionInfo> 结果 = new List<OnlineVersionInfo>();
        string json = 客户端.GetStringAsync(清单地址).Result;
        JsonDocument doc = JsonDocument.Parse(json);
        JsonElement 版本节点 = doc.RootElement.GetProperty("versions");
        foreach (JsonElement v in 版本节点.EnumerateArray())
        {
            string 类型 = v.GetProperty("type").GetString() ?? "";
            if (类型 != "release")
                continue;
            结果.Add(new OnlineVersionInfo
            {
                Id = v.GetProperty("id").GetString() ?? "",
                Url = v.GetProperty("url").GetString() ?? ""
            });
        }
        return 结果;
    }

    // 方法：拉某个版本的详情 json 原文字符串（供 Program 保存到本地）
    // 加了 try：网址 404、网络断了就返回空串，不让整个流程崩掉
    public string FetchVersionDetailRaw(string 详情地址)
    {
        try
        {
            return 客户端.GetStringAsync(详情地址).Result;
        }
        catch (Exception 异常)
        {
            Console.WriteLine("[详情拉取失败] " + 异常.Message);
            return "";
        }
    }

    // 方法：读取本地那份版本详情 json，抽出 client.jar 地址和 libraries 需下载信息
    public VersionDetailInfo LoadVersionDetailFromFile(string json本地路径)
    {
        string json = File.ReadAllText(json本地路径);
        VersionDetailInfo 详情 = new VersionDetailInfo();
        JsonDocument doc = JsonDocument.Parse(json);
        JsonElement 根 = doc.RootElement;

        // client.jar 在 downloads.client.url
        if (根.TryGetProperty("downloads", out JsonElement dl) &&
            dl.TryGetProperty("client", out JsonElement c) &&
            c.TryGetProperty("url", out JsonElement cu))
        {
            详情.ClientJarUrl = cu.GetString() ?? "";
        }

        // 遍历 libraries
        if (根.TryGetProperty("libraries", out JsonElement libs))
        {
            // 官方 json 里有同一个库写两遍的情况（比如 1.0 的 jinput-platform:2.0.5），
            // 这里记一下已经收过的下载地址，重复的直接跳过
            List<string> 已收地址 = new List<string>();

            foreach (JsonElement lib in libs.EnumerateArray())
            {
                // 先过 rules：不是 Windows 系统的库直接扔掉，别下
                if (!这个库要用(lib))
                {
                    continue;
                }

                string url = "";
                string path = "";
                string 名称 = lib.TryGetProperty("name", out JsonElement ne) ? (ne.GetString() ?? "") : "";

                // 有些 arm64 原生库在 rules 里只写了 windows、没单独写架构，
                // 只能靠名字认：非 arm 机器不需要 -arm64 结尾的那份
                if (名称.EndsWith("-arm64", StringComparison.OrdinalIgnoreCase) && !是Arm机器())
                {
                    continue;
                }

                // ---------- 情况1：老版本的原生库 ----------
                // natives 长这样 {"windows":"natives-windows", ...}，注意是小写 windows。
                // 真正下载地址不在 natives 里，在 downloads.classifiers["natives-windows"] 里。
                if (lib.TryGetProperty("natives", out JsonElement nat) &&
                    nat.ValueKind == JsonValueKind.Object &&
                    nat.TryGetProperty("windows", out JsonElement 分类名节点) &&
                    分类名节点.ValueKind == JsonValueKind.String)
                {
                    string 分类名 = 分类名节点.GetString() ?? "";
                    if (lib.TryGetProperty("downloads", out JsonElement 下载区1) &&
                        下载区1.TryGetProperty("classifiers", out JsonElement 分类表) &&
                        分类表.TryGetProperty(分类名, out JsonElement 分类项))
                    {
                        url = 分类项.TryGetProperty("url", out JsonElement u1) ? (u1.GetString() ?? "") : "";
                        path = 分类项.TryGetProperty("path", out JsonElement p1) ? (p1.GetString() ?? "") : "";
                    }
                }

                // ---------- 情况2：普通库 / 新版本原生库 ----------
                // 1.19 以后没 natives 字段了，真地址统一放 downloads.artifact
                if (string.IsNullOrEmpty(url) &&
                    lib.TryGetProperty("downloads", out JsonElement 下载区2) &&
                    下载区2.TryGetProperty("artifact", out JsonElement 构件))
                {
                    url = 构件.TryGetProperty("url", out JsonElement u2) ? (u2.GetString() ?? "") : "";
                    path = 构件.TryGetProperty("path", out JsonElement p2) ? (p2.GetString() ?? "") : "";
                }

                // ---------- 情况3：没有地址只有 maven 坐标，自己拼 ----------
                if (string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(名称) && 名称.Contains(':'))
                {
                    url = "https://libraries.minecraft.net/" + MavenToUnixPath(名称);
                    if (string.IsNullOrEmpty(path))
                    {
                        path = MavenToUnixPath(名称);
                    }
                }

                if (!string.IsNullOrEmpty(url))
                {
                    string 去重键 = string.IsNullOrEmpty(path) ? url : path;
                    if (!已收地址.Contains(去重键))
                    {
                        已收地址.Add(去重键);
                        详情.Libraries.Add(new LibraryInfo { Url = url, Path = path });
                    }
                }
            }
        }

        return 详情;
    }

    // 方法：判断一个库在当前系统（Windows）上要不要
    // MC 的规矩：有 rules 就一条条看，最后一条命中的规则说了算；
    //            一条都没命中 → 不要；压根没有 rules → 全平台都要
    private static bool 这个库要用(JsonElement lib)
    {
        if (!lib.TryGetProperty("rules", out JsonElement 规则表) ||
            规则表.ValueKind != JsonValueKind.Array)
        {
            return true;
        }

        bool 最后结论 = false;
        bool 是64位 = Environment.Is64BitProcess;
        bool 是Arm = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                        .ToString() == "Arm64";

        foreach (JsonElement 规则 in 规则表.EnumerateArray())
        {
            // 只看 os 条件，features（比如是不是演示模式）这类条件忽略
            if (规则.TryGetProperty("os", out JsonElement 系统条件))
            {
                string 系统名 = 系统条件.TryGetProperty("name", out JsonElement 名节点)
                    ? (名节点.GetString() ?? "")
                    : "";

                // 这条规则说的是别的系统（osx / linux），跟 Windows 无关，跳过
                if (!string.IsNullOrEmpty(系统名) && 系统名 != "windows")
                {
                    continue;
                }

                // 架构条件：x86 / x86_64 / arm64，对不上的跳过
                string 架构 = 系统条件.TryGetProperty("arch", out JsonElement 架节点)
                    ? (架节点.GetString() ?? "")
                    : "";

                if (架构 == "x86" && (是64位 && !是Arm))
                {
                    continue;   // 这是给 32 位的库
                }
                if ((架构 == "x86_64" || 架构 == "amd64") && !(是64位 && !是Arm))
                {
                    continue;   // 这是给 64 位 x86 的库
                }
                if ((架构 == "arm64" || 架构 == "aarch64") && !是Arm)
                {
                    continue;   // 这是给 arm64 的库
                }
            }

            最后结论 = 规则.TryGetProperty("action", out JsonElement 动作) &&
                       动作.GetString() == "allow";
        }

        return 最后结论;
    }

    // 方法：当前机器是不是 arm 架构
    private static bool 是Arm机器()
    {
        return System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                   .ToString() == "Arm64";
    }

    // 把 mavenName 形如 "com.foo:bar:1.0" 转成路径 com/foo/bar/1.0/bar-1.0.jar
    private static string MavenToUnixPath(string mavenName)
    {
        string[] parts = mavenName.Split(new[] { ':' }, 4);
        string groupId = parts[0].Replace('.', '/');
        string artifactId = parts[1];
        string version = parts[2];
        // 第 4 段是分类器（如 natives-windows），有就拼进文件名
        string 分类器 = parts.Length > 3 ? "-" + parts[3] : "";
        return groupId + "/" + artifactId + "/" + version + "/" + artifactId + "-" + version + 分类器 + ".jar";
    }
}

// 【在线版本信息：清单里的一个 release 条目】
public class OnlineVersionInfo
{
    public string Id = string.Empty;   // 版本编号，如 1.20.4
    public string Url = string.Empty;  // 版本详情 json 地址
}

// 【版本详情信息：要下载的文件集合】
public class VersionDetailInfo
{
    public string ClientJarUrl = string.Empty;  // client.jar 下载地址
    public List<LibraryInfo> Libraries = new List<LibraryInfo>();
}

// 【单个库文件：一个 jar 的下载信息】
public class LibraryInfo
{
    public string Url = string.Empty;  // 下载地址
    public string Path = string.Empty; // maven 坐标/相对路径
}
