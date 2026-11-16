using System;
using Microsoft.UI.Xaml.Controls;

using FluentSensors.Common.Localization;

namespace FluentSensors.Common.UI
{
    // the view options in the overflow menu of the sensors and graphs pages:
    // radio items for the flyout of one overflow entry (the entry shows its own submenu chevron); the pages refill the
    // flyout on every Opening, so the mark always shows the saved value
    public static class ViewOptionsMenu
    {
        // Automatic (null) or 0 to 3 decimals
        public static void FillDecimalPlaces(MenuFlyout flyout, int? current, Action<int?> apply)
        {
            const string group = "DecimalPlaces";

            flyout.Items.Clear();
            flyout.Items.Add(CreateRadioItem(AppStrings.Get("ViewMenu_DecimalPlacesAuto"), group, current == null, () => apply(null)));
            for (int places = 0; places <= 3; places++)
            {
                int value = places;
                flyout.Items.Add(CreateRadioItem(value.ToString(), group, current == value, () => apply(value)));
            }
        }

        // the y-axis maximum beside the title or beside the value
        public static void FillYMaxPosition(MenuFlyout flyout, bool besideValue, Action<bool> apply)
        {
            const string group = "YMaxPosition";

            flyout.Items.Clear();
            flyout.Items.Add(CreateRadioItem(AppStrings.Get("ViewMenu_YMaxBesideTitle"), group, !besideValue, () => apply(false)));
            flyout.Items.Add(CreateRadioItem(AppStrings.Get("ViewMenu_YMaxBesideValue"), group, besideValue, () => apply(true)));
        }

        private static RadioMenuFlyoutItem CreateRadioItem(string text, string group, bool isChecked, Action onClick)
        {
            var item = new RadioMenuFlyoutItem { Text = text, GroupName = group, IsChecked = isChecked };
            item.Click += (s, e) => onClick();
            return item;
        }
    }
}
