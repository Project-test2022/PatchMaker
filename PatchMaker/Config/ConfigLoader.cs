using System;
using System.IO;
using System.Text.Json;

namespace PatchMaker.Config
{
    public static class ConfigLoader
    {
        public static MakerConfig LoadOrPrompt(string path)
        {
            MakerConfig config;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                config = JsonSerializer.Deserialize<MakerConfig>(json, new JsonSerializerOptions{ PropertyNameCaseInsensitive = true });
                if (config == null)
                {
                    config = new MakerConfig();
                }
                PromptIfEmpty(config);
            }
            else
            {
                config = new MakerConfig();
                Console.WriteLine("appsettings.json がないため、設定を対話形式で入力します。");
                PromptIfEmpty(config);

                Console.Write("設定を appsettings.json に保存しますか？ (y/N): ");
                var input = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(input) && input.Trim().ToLowerInvariant() == "y")
                {
                    var outJson = JsonSerializer.Serialize(config, new JsonSerializerOptions{ WriteIndented = true });
                    File.WriteAllText(path, outJson);
                    Console.WriteLine("設定を保存しました: " + Path.GetFullPath(path));
                }
            }
            return config;
        }

        private static void PromptIfEmpty(MakerConfig config)
        {
            config.OldDir = Ask("旧バージョンのディレクトリを入力してください", config.OldDir);
            config.NewDir = Ask("新バージョンのディレクトリを入力してください", config.NewDir);
            config.OutDir = Ask("出力先のディレクトリを入力してください", string.IsNullOrWhiteSpace(config.OutDir) ? "dist" : config.OutDir);
            config.BaseVersion = Ask("ベースバージョンを入力してください（例: 1.2.3）", config.BaseVersion);
            config.Version = Ask("新バージョンを入力してください（例: 1.2.4）", config.Version);
        }

        private static string Ask(string label, string current)
        {
            Console.Write(label + (string.IsNullOrWhiteSpace(current) ? "" : (" [" + current + "]")) + ": ");
            var input = Console.ReadLine();
            return string.IsNullOrWhiteSpace(input) ? current : input.Trim();
        }
    }
}
