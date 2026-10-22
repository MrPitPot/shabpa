using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>"Name and birthday" in Settings: an optional name, a birthday (Jalali or Gregorian, the year is optional), a
/// preview of the celebration, and a switch for the festive look. Stored on this PC only; nothing here uses the network.</summary>
public sealed class BirthdayCard : GlassCard
{
    private static Style St(string key) => (Style)Application.Current.Resources[key];
    private static Brush Res(string key) => Ui.Live(key);

    private readonly TextBox _name = new() { Style = St("Input"), MaxLength = BirthdaySettings.MaxName };
    private readonly TextBox _day = new() { Style = St("Input"), MaxLength = 2, TextAlignment = TextAlignment.Center };
    private readonly TextBox _year = new() { Style = St("Input"), MaxLength = 4, TextAlignment = TextAlignment.Center };
    private readonly Panel _calendar = new StackPanel { Orientation = Orientation.Horizontal };
    private readonly WrapPanel _months = new();
    private readonly TextBlock _status = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private readonly Switch _look = new();
    private bool _jalali;
    private int _month;

    /// <summary>Test aid: the editing fields.</summary>
    internal (string name, bool jalali, int month, string day, string year) Fields => (_name.Text, _jalali, _month, _day.Text, _year.Text);

    public BirthdayCard()
    {
        Margin = new Thickness(0);
        Padding = new Thickness(22, 20, 22, 18);
        var s = Hub.I.Settings.Birthday;
        _jalali = s.HasDate ? s.Jalali : !L.En; // Persian calendar by default in Persian, Gregorian in English

        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = L.T("نام و تاریخ تولد", "Name and birthday"), Style = St("H2") });
        body.Children.Add(new TextBlock
        {
            Text = L.T("اختیاریه. اگه پرش کنی، شب‌پا روز تولدت بهت تبریک می‌گه و اون روز ظاهرش جشنی می‌شه.",
                "Optional. Fill it in and Shabpa congratulates you on your birthday and dresses up for the day."),
            Style = St("Body"), Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap,
        });

        body.Children.Add(Labelled(L.T("نام (اختیاری، تا ۳۰ حرف)", "Name (optional, up to 30 characters)"), _name, 14));
        body.Children.Add(Labelled(L.T("تقویم", "Calendar"), _calendar, 12));
        body.Children.Add(Labelled(L.T("ماه", "Month"), _months, 12));

        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(-6, 4, -6, 0) };
        var dayPanel = Labelled(L.T("روز", "Day"), _day, 0);
        dayPanel.Margin = new Thickness(6, 0, 6, 0);
        var yearPanel = Labelled(L.T("سال تولد (اختیاری)", "Birth year (optional)"), _year, 0);
        yearPanel.Margin = new Thickness(6, 0, 6, 0);
        grid.Children.Add(dayPanel);
        grid.Children.Add(yearPanel);
        body.Children.Add(grid);
        body.Children.Add(_status);

        var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        var save = new Button { Style = St("BtnPrimary"), Content = L.T("ذخیره", "Save"), Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(26, 9, 26, 9) };
        var preview = new Button { Style = St("Btn"), Content = L.T("پیش‌نمایش جشن تولد", "Preview the birthday"), Margin = new Thickness(0, 0, 8, 8) };
        var clear = new Button { Style = St("BtnDanger"), Content = L.T("پاک کردن", "Clear"), Margin = new Thickness(0, 0, 0, 8) };
        save.Click += (_, _) => Save();
        preview.Click += (_, _) => (Application.Current.MainWindow as MainWindow)?.ShowBirthdayGreeting(Birthday.CleanName(_name.Text));
        clear.Click += (_, _) => Clear();
        buttons.Children.Add(save);
        buttons.Children.Add(preview);
        buttons.Children.Add(clear);
        body.Children.Add(buttons);

        _look.Content = new TextBlock { Text = L.T("تم تولد در روز تولد", "Birthday theme on my birthday"), TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.Medium, Margin = new Thickness(0, 6, 0, 6) };
        _look.HorizontalAlignment = HorizontalAlignment.Stretch;
        _look.MinHeight = 44;
        _look.ToolTip = L.T("فقط همون یک روز ظاهر برنامه جشنی می‌شه؛ تم انتخابی‌ت عوض نمی‌شه و فردا خودش برمی‌گرده.",
            "Only for that one day the app dresses up; your chosen theme is not changed and comes back by itself tomorrow.");
        _look.IsChecked = s.Theme;
        _look.Click += (_, _) =>
        {
            Hub.I.Settings.Birthday.Theme = _look.IsChecked == true;
            Hub.I.Store.Save();
            Hub.I.RecheckBirthday();
        };
        var lookRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        lookRow.Children.Add(_look);
        lookRow.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Background = Res("Line"), IsHitTestVisible = false });
        body.Children.Add(lookRow);

        body.Children.Add(new TextBlock
        {
            Text = L.T("فقط روی همین کامپیوتر ذخیره می‌شه و هیچ‌جا ارسال نمی‌شه.", "Stored only on this PC, never sent anywhere."),
            Style = St("Tip"), Margin = new Thickness(0, 8, 0, 0),
        });
        Child = body;

        _name.Text = s.Name;
        Load(s);
        BuildCalendar();
        BuildMonths();
        UpdateStatus();
    }

    /// <summary>Shows the saved values again (after they were changed from outside, e.g. by a test).</summary>
    internal void Reload()
    {
        var s = Hub.I.Settings.Birthday;
        _jalali = s.HasDate ? s.Jalali : !L.En;
        _name.Text = s.Name;
        _look.IsChecked = s.Theme;
        Load(s);
        BuildCalendar();
        BuildMonths();
        UpdateStatus();
    }

    private static StackPanel Labelled(string label, UIElement control, double top)
    {
        var panel = new StackPanel { Margin = new Thickness(0, top, 0, 0) };
        panel.Children.Add(new TextBlock { Text = label, Style = St("Label"), Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(control);
        return panel;
    }

    private void Load(BirthdaySettings s)
    {
        _month = s.HasDate ? s.Month : 0;
        _day.Text = s.HasDate ? Fa.Digits(s.Day.ToString()) : "";
        _year.Text = s.HasDate && s.Year > 0 ? Fa.Digits(s.Year.ToString()) : "";
    }

    private void BuildCalendar() =>
        PageBase.Chips(_calendar, [(true, L.T("شمسی", "Jalali")), (false, L.T("میلادی", "Gregorian"))], _jalali, jalali =>
        {
            if (jalali == _jalali) return;
            // keep the same birthday: the typed date is moved to the other calendar when it is complete and valid
            var temp = new BirthdaySettings { Jalali = _jalali, Month = _month, Day = Number(_day.Text), Year = Number(_year.Text) };
            if (temp.HasDate) { Birthday.Convert(temp, jalali, Birthday.Now()); Load(temp); }
            else _month = 0;
            _jalali = jalali;
            BuildMonths();
            UpdateStatus();
        });

    private void BuildMonths() =>
        PageBase.Chips(_months, Enumerable.Range(1, 12).Select(m => (m, Birthday.MonthName(_jalali, m))), _month, m => { _month = m; ShowError(null); });

    private static int Number(string text) => int.TryParse(Birthday.LatinDigits(text), out var n) ? n : 0;

    /// <summary>The form as typed, or an error message in the person's language.</summary>
    private (BirthdaySettings? value, string? error) Read()
    {
        var b = new BirthdaySettings { Name = Birthday.CleanName(_name.Text), Jalali = _jalali, Theme = _look.IsChecked == true };
        var dayText = Birthday.LatinDigits(_day.Text);
        var yearText = Birthday.LatinDigits(_year.Text);
        if (_month == 0 && dayText.Length == 0 && yearText.Length == 0) return (b, null); // name only (or nothing)
        if (_month == 0) return (null, L.T("ماه تولد رو انتخاب کن.", "Pick the birth month."));
        b.Month = _month;
        var max = Birthday.MaxDay(_jalali, _month);
        if (!int.TryParse(dayText, out var day) || day < 1 || day > max)
            return (null, L.T($"روز باید بین ۱ تا {Fa.Digits(max.ToString())} باشه.", $"The day must be between 1 and {max}."));
        b.Day = day;
        if (yearText.Length > 0)
        {
            if (!int.TryParse(yearText, out var year)) return (null, L.T("سال درست نیست.", "That year is not valid."));
            b.Year = year;
            if (!Birthday.YearOk(b))
                return (null, L.T("این روز تو اون سال وجود نداشته (یا سال معتبر نیست). سال رو خالی بذار یا درستش کن.", "That day did not exist in that year (or the year is not valid). Leave the year empty or fix it."));
        }
        return (b, null);
    }

    private void Save()
    {
        var (value, error) = Read();
        if (value is null) { ShowError(error); return; }
        var s = Hub.I.Settings.Birthday;
        var moved = s.Jalali != value.Jalali || s.Month != value.Month || s.Day != value.Day;
        s.Name = value.Name;
        s.Jalali = value.Jalali;
        s.Month = value.Month;
        s.Day = value.Day;
        s.Year = value.Year;
        if (moved) { s.LastGreeted = ""; s.LastBalloon = ""; } // a new date is celebrated on its own day
        _name.Text = s.Name;
        Hub.I.Store.Save();
        Hub.I.RecheckBirthday();
        UpdateStatus();
        Motion.Rise(_status, 0, 8, 0.45);
    }

    private void Clear()
    {
        var s = Hub.I.Settings.Birthday;
        if (s.IsEmpty && _name.Text.Length == 0 && _month == 0 && _day.Text.Length == 0) return;
        if (!ConfirmWindow.Ask(L.T("نام و تاریخ تولد پاک بشه؟", "Clear the name and birthday?"),
                L.T("هر دو از این کامپیوتر پاک می‌شن و دیگه تبریکی در کار نیست. تم تولد هم اگه امروز روشن باشه برمی‌گرده.",
                    "Both are removed from this PC and there will be no greeting. If the birthday look is on today, it goes back too."),
                L.T("پاک کن", "Clear"), danger: true)) return;
        var look = s.Theme;
        Hub.I.Settings.Birthday = new BirthdaySettings { Jalali = !L.En, Theme = look };
        _jalali = !L.En;
        _name.Text = "";
        Load(Hub.I.Settings.Birthday);
        BuildCalendar();
        BuildMonths();
        Hub.I.Store.Save();
        Hub.I.RecheckBirthday();
        UpdateStatus();
    }

    private void ShowError(string? text)
    {
        if (text is null) { UpdateStatus(); return; }
        _status.Foreground = Res("Red");
        _status.Text = text;
        Motion.Rise(_status, 0, 8, 0.45);
    }

    private void UpdateStatus()
    {
        _status.Foreground = Res("Muted");
        var s = Hub.I.Settings.Birthday;
        if (!s.HasDate)
        {
            _status.Text = s.Name.Length > 0
                ? L.T("نام ذخیره شد. برای تبریک، ماه و روز تولد هم لازمه.", "Name saved. For the greeting the birth month and day are needed too.")
                : L.T("هنوز چیزی ثبت نشده؛ بدون این‌ها هیچ تبریکی در کار نیست.", "Nothing saved yet; without it there is no greeting.");
            return;
        }
        var now = Birthday.Now();
        var next = Birthday.Next(s, now)!.Value;
        var days = (int)(next - now.Date).TotalDays;
        var on = $"{Fa.Digits(s.Day.ToString())} {Birthday.MonthName(s.Jalali, s.Month)}";
        _status.Text = days == 0
            ? L.T($"تولدت {on}؛ امروزه!", $"Your birthday is {on}: it is today!")
            : L.T($"تولدت {on}؛ {Fa.Digits(days.ToString())} روز دیگه.", $"Your birthday is {on}: in {days} day{(days == 1 ? "" : "s")}.");
    }
}
