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
    private readonly Panel _calendar = new StackPanel { Orientation = Orientation.Horizontal };
    private readonly Switch _dateOn = new();
    private readonly StackPanel _pickerBox = new();
    private readonly WheelColumn _dayCol = new(L.T("روز", "Day"));
    private readonly WheelColumn _monthCol = new(L.T("ماه", "Month"));
    private readonly WheelColumn _yearCol = new(L.T("سال", "Year"));
    private readonly TextBlock _status = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private readonly Switch _look = new();
    private bool _jalali;
    private bool _hasDate;
    private int _month = 1, _day = 1, _year;
    private int _yearLo, _yearHi;

    /// <summary>Test aid: the editing fields (month 0 = no date chosen; the year is empty when "no year" is picked).</summary>
    internal (string name, bool jalali, int month, string day, string year) Fields =>
        (_name.Text, _jalali, _hasDate ? _month : 0, _hasDate ? _day.ToString() : "", _hasDate && _year > 0 ? _year.ToString() : "");

    /// <summary>Test aid: the three wheels (day, month, year).</summary>
    internal (WheelColumn day, WheelColumn month, WheelColumn year) Wheels => (_dayCol, _monthCol, _yearCol);

    public BirthdayCard()
    {
        Margin = new Thickness(0);
        Padding = new Thickness(22, 20, 22, 18);
        var s = Hub.I.Settings.Birthday;

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

        _dateOn.Content = new TextBlock { Text = L.T("تاریخ تولدم رو هم ثبت کن", "Also save my birthday"), TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.Medium, Margin = new Thickness(0, 6, 0, 6) };
        _dateOn.HorizontalAlignment = HorizontalAlignment.Stretch;
        _dateOn.MinHeight = 44;
        _dateOn.Click += (_, _) => SetDateOn(_dateOn.IsChecked == true);
        body.Children.Add(new Grid { Margin = new Thickness(0, 10, 0, 0), Children = { _dateOn } });

        // the picker: day, month, year (the first column sits at the start edge); the year wheel starts with "no year"
        var heads = new Grid { FlowDirection = Ui.Flow, Margin = new Thickness(8, 0, 8, 4) };
        var picker = new WheelPicker();
        foreach (var (col, label, weight) in new[] { (_dayCol, L.T("روز", "Day"), 0.8), (_monthCol, L.T("ماه", "Month"), 1.5), (_yearCol, L.T("سال (اختیاری)", "Year (optional)"), 1.25) })
        {
            heads.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(weight, GridUnitType.Star) });
            var head = new TextBlock { Text = label, Style = St("Label"), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(head, heads.ColumnDefinitions.Count - 1);
            heads.Children.Add(head);
            picker.Add(col, weight);
        }
        _dayCol.Changed += i => { _day = i + 1; ShowError(null); };
        _monthCol.Changed += i => { _month = i + 1; RefreshDays(true); ShowError(null); };
        _yearCol.Changed += i => { _year = WheelMath.YearAt(i, _yearHi); RefreshDays(true); ShowError(null); };
        _pickerBox.Margin = new Thickness(0, 4, 0, 0);
        _pickerBox.Width = 340;
        picker.HorizontalAlignment = HorizontalAlignment.Stretch;
        _pickerBox.HorizontalAlignment = HorizontalAlignment.Left;
        _pickerBox.Children.Add(heads);
        _pickerBox.Children.Add(picker);
        body.Children.Add(_pickerBox);
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

        Reload();
    }

    /// <summary>Shows the saved values again (after they were changed from outside, e.g. by a test).</summary>
    internal void Reload()
    {
        var s = Hub.I.Settings.Birthday;
        _jalali = s.HasDate ? s.Jalali : !L.En; // Persian calendar by default in Persian, Gregorian in English
        _name.Text = s.Name;
        _look.IsChecked = s.Theme;
        _hasDate = s.HasDate;
        Load(s);
        BuildCalendar();
        BuildWheels(false);
        _dateOn.IsChecked = _hasDate;
        _pickerBox.Visibility = _hasDate ? Visibility.Visible : Visibility.Collapsed;
        UpdateStatus();
    }

    private static StackPanel Labelled(string label, UIElement control, double top)
    {
        var panel = new StackPanel { Margin = new Thickness(0, top, 0, 0) };
        panel.Children.Add(new TextBlock { Text = label, Style = St("Label"), Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(control);
        return panel;
    }

    /// <summary>The wheels' state from saved values; with no date they start on today's day and month.</summary>
    private void Load(BirthdaySettings s)
    {
        var now = Birthday.Now();
        _month = s.HasDate ? s.Month : Birthday.MonthOf(_jalali, now);
        _day = s.HasDate ? s.Day : Birthday.DayOf(_jalali, now);
        _year = s.HasDate ? s.Year : 0;
    }

    private void SetDateOn(bool on)
    {
        _hasDate = on;
        _dateOn.IsChecked = on;
        _pickerBox.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on)
        {
            BuildWheels(false);
            Motion.Rise(_pickerBox, 0, 10, 0.45);
        }
        ShowError(null);
    }

    private void BuildCalendar() =>
        PageBase.Chips(_calendar, [(true, L.T("شمسی", "Jalali")), (false, L.T("میلادی", "Gregorian"))], _jalali, jalali =>
        {
            if (jalali == _jalali) return;
            // keep the same birthday: the chosen date is moved to the other calendar
            var temp = new BirthdaySettings { Jalali = _jalali, Month = _month, Day = _day, Year = _year };
            if (_hasDate && temp.HasDate) Birthday.Convert(temp, jalali, Birthday.Now());
            else
            {
                temp.Jalali = jalali;
                temp.Month = Birthday.MonthOf(jalali, Birthday.Now());
                temp.Day = Birthday.DayOf(jalali, Birthday.Now());
                temp.Year = 0;
            }
            _jalali = jalali;
            _month = temp.Month;
            _day = temp.Day;
            _year = temp.Year;
            BuildWheels(true);
            UpdateStatus();
        });

    /// <summary>Fills the month, year and day wheels for the current calendar and selection.</summary>
    private void BuildWheels(bool animate)
    {
        _monthCol.SetItems(Enumerable.Range(1, 12).Select(m => Birthday.MonthName(_jalali, m)).ToList(), _month - 1, animate);
        (_yearLo, _yearHi) = WheelMath.YearRange(_jalali, Birthday.Now(), _year);
        var years = new List<string> { L.T("بدون سال", "No year") };
        for (var y = _yearHi; y >= _yearLo; y--) years.Add(Fa.Digits(y.ToString()));
        _yearCol.SetItems(years, WheelMath.YearIndex(_year, _yearLo, _yearHi), animate);
        RefreshDays(animate, true);
    }

    /// <summary>The day wheel follows the month and the year: Esfand 30 and Feb 29 exist only where they should.</summary>
    private void RefreshDays(bool animate, bool force = false)
    {
        var count = WheelMath.DayCount(_jalali, _month, _year);
        _day = WheelMath.ClampDay(_day, count);
        if (force || _dayCol.Count != count)
            _dayCol.SetItems(Enumerable.Range(1, count).Select(d => Fa.Digits(d.ToString())).ToList(), _day - 1, animate);
        else _dayCol.Select(_day - 1, animate);
    }

    /// <summary>The form as typed, or an error message in the person's language.</summary>
    private (BirthdaySettings? value, string? error) Read()
    {
        var b = new BirthdaySettings { Name = Birthday.CleanName(_name.Text), Jalali = _jalali, Theme = _look.IsChecked == true };
        if (!_hasDate) return (b, null); // name only (or nothing)
        b.Month = _month;
        b.Day = _day;
        b.Year = _year;
        if (!Birthday.YearOk(b))
            return (null, L.T("این روز تو اون سال وجود نداشته (یا سال معتبر نیست). «بدون سال» رو انتخاب کن یا درستش کن.", "That day did not exist in that year (or the year is not valid). Pick “No year” or fix it."));
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
        if (s.IsEmpty && _name.Text.Length == 0 && !_hasDate) return;
        if (!ConfirmWindow.Ask(L.T("نام و تاریخ تولد پاک بشه؟", "Clear the name and birthday?"),
                L.T("هر دو از این کامپیوتر پاک می‌شن و دیگه تبریکی در کار نیست. تم تولد هم اگه امروز روشن باشه برمی‌گرده.",
                    "Both are removed from this PC and there will be no greeting. If the birthday look is on today, it goes back too."),
                L.T("پاک کن", "Clear"), danger: true)) return;
        var look = s.Theme;
        Hub.I.Settings.Birthday = new BirthdaySettings { Jalali = !L.En, Theme = look };
        _jalali = !L.En;
        _name.Text = "";
        _hasDate = false;
        Load(Hub.I.Settings.Birthday);
        BuildCalendar();
        BuildWheels(false);
        _dateOn.IsChecked = false;
        _pickerBox.Visibility = Visibility.Collapsed;
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
