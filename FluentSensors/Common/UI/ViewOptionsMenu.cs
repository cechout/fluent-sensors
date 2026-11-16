using System;
using Microsoft.UI.Xaml.Controls;

using FluentSensors.Common.Localization;

namespace FluentSensors.Common.UI
{
    // the view menu of the sensors and graphs pages:
    // submenus of radio items for settings that belong to one page; the pages rebuild them on every Opening, so the
    // marks always show the saved value
    public static class ViewOptionsMenu
    {
        // Automatic (null) or 0 to 3 decimals
        public static MenuFlyoutSubItem CreateDecimalPlacesItem(int? current, Action<int?> apply)
        {
            var subItem = new MenuFlyoutSubItem { Text = AppStrings.Get("ViewMenu_DecimalPlaces") };
            const string group = "DecimalPlaces";

            subItem.Items.Add(CreateRadioItem(AppStrings.Get("ViewMenu_DecimalPlacesAuto"), group, current == null, () => apply(null)));
            for (int places = 0; places <= 3; places++)
            {
                int value = places;
                subItem.Items.Add(CreateRadioItem(value.ToString(), group, current == value, () => apply(value)));
            }

            return subItem;
        }

        // the y-axis maximum beside the title or beside the value
        public static MenuFlyoutSubItem CreateYMaxPositionItem(bool besideValue, Action<bool> apply)
        {
            var subItem = new MenuFlyoutSubItem { Text = AppStrings.Get("ViewMenu_YMaxPosition") };
            const string group = "YMaxPosition";

            subItem.Items.Add(CreateRadioItem(AppStrings.Get("ViewMenu_YMaxBesideTitle"), group, !besideValue, () => apply(false)));
            subItem.Items.Add(CreateRadioItem(AppStrings.Get("ViewMenu_YMaxBesideValue"), group, besideValue, () => apply(true)));

            return subItem;
        }

        private static RadioMenuFlyoutItem CreateRadioItem(string text, string group, bool isChecked, Action onClick)
        {
            var item = new RadioMenuFlyoutItem { Text = text, GroupName = group, IsChecked = isChecked };
            item.Click += (s, e) => onClick();
            return item;
        }
    }
}
