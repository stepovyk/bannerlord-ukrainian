using System;
using System.IO;
using System.Reflection;
using TaleWorlds.Localization;
using TaleWorlds.Localization.TextProcessor;
using TaleWorlds.ModuleManager;
using TaleWorlds.MountAndBlade;

namespace UkrainianLocalization
{
    public class UkrainianSubModule : MBSubModuleBase
    {
        public const string ModuleId = "UkrainianLocalization";
        public const string LanguageId = "Українська";

        private static readonly FieldInfo ProcessorField =
            typeof(MBTextManager).GetField("_languageProcessor", BindingFlags.NonPublic | BindingFlags.Static);

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            InstallSubtitles();
            EnsureProcessor();
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            EnsureProcessor();
        }

        public override void OnConfigChanged()
        {
            base.OnConfigChanged();
            EnsureProcessor();
        }

        /// <summary>
        /// The language data names this processor by type, so the game creates it itself. This is a safety net
        /// for the case the runtime could not resolve the type name (it falls back to a processor without grammar).
        /// </summary>
        public static void EnsureProcessor()
        {
            try
            {
                if (ProcessorField == null || MBTextManager.ActiveTextLanguage != LanguageId)
                {
                    return;
                }
                if (!(ProcessorField.GetValue(null) is UkrainianTextProcessor))
                {
                    ProcessorField.SetValue(null, new UkrainianTextProcessor());
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Video subtitles are looked up next to the SandBox videos only, so the Ukrainian .srt files
        /// shipped with this module are placed there (new files only; nothing of the game is overwritten).
        /// </summary>
        private static void InstallSubtitles()
        {
            try
            {
                string modulePath = ModuleHelper.GetModuleFullPath(ModuleId);
                string sandBoxPath = ModuleHelper.GetModuleFullPath("SandBox");
                string source = Path.Combine(modulePath, "Videos");
                if (!Directory.Exists(source))
                {
                    return;
                }
                foreach (string file in Directory.GetFiles(source, "*.srt", SearchOption.AllDirectories))
                {
                    string relative = file.Substring(source.Length).TrimStart('/', '\\');
                    string target = Path.Combine(sandBoxPath, "Videos", relative);
                    string targetDir = Path.GetDirectoryName(target);
                    if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
                    {
                        continue;
                    }
                    if (File.Exists(target) && new FileInfo(target).Length == new FileInfo(file).Length
                        && File.ReadAllText(target) == File.ReadAllText(file))
                    {
                        continue;
                    }
                    File.Copy(file, target, true);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
