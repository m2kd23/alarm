using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace InternalAlarmApp.Views;

internal static class DatePickerAlignment
{
    public static void Center(DatePicker picker)
    {
        picker.ApplyTemplate();

        if (picker.Template.FindName("PART_TextBox", picker) is DatePickerTextBox textBox)
        {
            textBox.TextAlignment = TextAlignment.Center;
            textBox.VerticalContentAlignment = VerticalAlignment.Center;
            textBox.Padding = new Thickness(0);

            textBox.ApplyTemplate();
            if (textBox.Template.FindName("PART_Watermark", textBox) is ContentControl watermark)
            {
                watermark.HorizontalContentAlignment = HorizontalAlignment.Center;
                watermark.VerticalContentAlignment = VerticalAlignment.Center;
            }
        }

        if (picker.Template.FindName("PART_Button", picker) is Button calendarButton)
        {
            calendarButton.VerticalAlignment = VerticalAlignment.Center;
            calendarButton.VerticalContentAlignment = VerticalAlignment.Center;
            calendarButton.Padding = new Thickness(0);
        }
    }
}
