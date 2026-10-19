using System;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ModuleManager;
using TaleWorlds.MountAndBlade.View;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.MountAndBlade.ViewModelCollection.Credits;
using TaleWorlds.ScreenSystem;
using TaleWorlds.TwoDimension;

namespace UkrainianLocalization
{
    /// <summary>
    /// The game's «Credits» screen with a «Ukrainian localization» section added before the legal notices.
    /// Same behaviour as the game's own credits screen; the section appears only while Ukrainian is the active language.
    /// </summary>
    [OverrideView(typeof(CreditsScreen))]
    public class UkrainianCreditsScreen : ScreenBase
    {
        private CreditsVM _datasource;

        private GauntletLayer _gauntletLayer;

        private SpriteCategory _creditsCategory;

        protected override void OnInitialize()
        {
            base.OnInitialize();
            _creditsCategory = UIResourceManager.LoadSpriteCategory("ui_credits");
            _datasource = new CreditsVM();
            _datasource.FillFromFile(ModuleHelper.GetModuleFullPath("Native") + "ModuleData/Credits.xml");
            AddLocalizationCredits(_datasource);
            _gauntletLayer = new GauntletLayer("CreditsScreen", 100, false);
            _gauntletLayer.IsFocusLayer = true;
            AddLayer(_gauntletLayer);
            _gauntletLayer.InputRestrictions.SetInputRestrictions(true, (InputUsageMask)7);
            ScreenManager.TrySetFocus(_gauntletLayer);
            _gauntletLayer.LoadMovie("CreditsScreen", _datasource);
            _gauntletLayer.Input.RegisterHotKeyCategory(HotKeyManager.GetCategory("GenericPanelGameKeyCategory"));
            InformationManager.HideAllMessages();
        }

        protected override void OnFinalize()
        {
            base.OnFinalize();
            _creditsCategory?.Unload();
            _datasource?.OnFinalize();
        }

        protected override void OnFrameTick(float dt)
        {
            base.OnFrameTick(dt);
            if (_gauntletLayer != null && _gauntletLayer.Input.IsHotKeyPressed("Exit"))
            {
                ScreenManager.PopScreen();
            }
        }

        private static void AddLocalizationCredits(CreditsVM credits)
        {
            try
            {
                CreditsItemVM root = credits.RootItem;
                if (root == null || MBTextManager.ActiveTextLanguage != UkrainianSubModule.LanguageId)
                {
                    return;
                }
                CreditsItemVM category = Item("Category", "Українська локалізація");
                category.Items.Add(Item("EmptyLine", ""));
                CreditsItemVM section = Item("Section", "Переклад, редагування, граматика й шрифти");
                section.Items.Add(Item("Entry", "Stepovyk"));
                category.Items.Add(section);
                category.Items.Add(Item("EmptyLine", ""));
                category.Items.Add(Item("Entry", "Дякуємо, що граєте українською!"));
                category.Items.Add(Item("EmptyLine", ""));

                // before the legal notices that close the game's credits
                int index = root.Items.Count;
                if (index > 0 && root.Items[index - 1] != null && root.Items[index - 1].Type != "Category")
                {
                    index--;
                }
                root.Items.Insert(index, category);
            }
            catch (Exception)
            {
            }
        }

        private static CreditsItemVM Item(string type, string text)
        {
            return new CreditsItemVM { Type = type, Text = text };
        }
    }
}
