using System.Globalization;
using System.Text.Json;

namespace Shabpa.Tests;

/// <summary>The typed-command parser, run over a few hundred sentences in Persian and English. Each line reads
/// "sentence => expectation"; the expectations are explained in <see cref="Check"/>.</summary>
public class CommandParserTests
{
    /// <summary>Thursday 22 October 2026 (30 Mehr 1405), 21:00.</summary>
    private static readonly DateTime Now = new(2026, 10, 22, 21, 0, 0);

    public static readonly string[] Persian =
    [
        // ---- power, right now ----
        "خاموش کن => power shutdown now",
        "سیستم رو خاموش کن => power shutdown now",
        "سیستمو خاموش کن => power shutdown now",
        "کامپیوتر رو خاموش کن لطفا => power shutdown now",
        "خاموشش کن => power shutdown now",
        "همین الان خاموش کن => power shutdown now",
        "خاموووش کن => power shutdown now",
        "خاموس کن => power shutdown now",
        "شات دان کن => power shutdown now",
        "بخوابون => power sleep now",
        "سیستم رو بخوابون => power sleep now",
        "بخوابش => power sleep now",
        "ببرش رو حالت خواب => power sleep now",
        "اسلیپ کن => power sleep now",
        "هایبرنیت کن => power hibernate now",
        "هایبرنت کن => power hibernate now",
        "ببرش خواب عمیق => power hibernate now",
        "ری استارت کن => power restart now",
        "ری‌استارت کن => power restart now",
        "ریستارت کن => power restart now",
        "سیستم رو ری استارت کن => power restart now",
        "راه اندازی مجدد => power restart now",
        "راه‌اندازی مجدد کن => power restart now",
        "ریبوت کن => power restart now",
        "صفحه رو قفل کن => power lock now",
        "قفلش کن => power lock now",
        "قفل کن => power lock now",
        "لاک کن => power lock now",
        "سيستم رو خاموش كن => power shutdown now",
        "شبپا سیستمو خاموش کن => power shutdown now",

        // ---- power, after a delay ----
        "۳۰ دقیقه دیگه خاموش کن => power shutdown +30m",
        "30 دقیقه دیگه خاموش کن => power shutdown +30m",
        "٣٠ دقيقه ديگه خاموش كن => power shutdown +30m",
        "سی دقیقه دیگه خاموش کن => power shutdown +30m",
        "نیم ساعت دیگه خاموش کن => power shutdown +30m",
        "یه ساعت دیگه خاموش کن => power shutdown +60m",
        "یک ساعت دیگر خاموش کن => power shutdown +60m",
        "یه ساعت و ربع دیگه خاموش کن => power shutdown +75m",
        "یک ساعت و نیم دیگه بخوابون => power sleep +90m",
        "دو ساعت دیگه خاموش کن => power shutdown +120m",
        "دو ساعت و بیست دقیقه دیگه خاموش کن => power shutdown +140m",
        "یه ربع دیگه بخوابون => power sleep +15m",
        "ربع ساعت دیگه خاموش کن => power shutdown +15m",
        "سه ربع دیگه خاموش کن => power shutdown +45m",
        "بیست و پنج دقیقه دیگه خاموش کن => power shutdown +25m",
        "۴۵ دقیقه بعد خاموش کن => power shutdown +45m",
        "بعد از ۲۰ دقیقه خاموش کن => power shutdown +20m",
        "تا ۱۰ دقیقه دیگه خاموش شه => power shutdown +10m",
        "۱۰ دقیقه‌ی دیگه خاموش کن => power shutdown +10m",
        "۱۰دقیقه دیگه خاموش => power shutdown +10m",
        "۵ دیقه دیگه بخوابون => power sleep +5m",
        "خاموش کن ۳۰ دقیقه دیگه => power shutdown +30m",
        "۱.۵ ساعت دیگه خاموش کن => power shutdown +90m",
        "۱٫۵ ساعت دیگه خاموش کن => power shutdown +90m",
        "۲ ساعت دیگه ری استارت کن => power restart +120m",
        "یه ساعت دیگه قفلش کن => power lock +60m",
        "۹۰ دقیقه دیگه هایبرنیت => power hibernate +90m",
        "۳۰ ثانیه دیگه خاموش کن => power shutdown +0.5m",
        "۳ ساعت و ۱۵ دقیقه دیگه خاموش کن => power shutdown +195m",
        "۴۰ دقیقه خاموش کن => power shutdown +40m",
        "ظرف ۲۰ دقیقه خاموش کن => power shutdown +20m",
        "پونزده دقیقه دیگه بخوابون => power sleep +15m",
        "شصت دقیقه دیگه خاموش کن => power shutdown +60m",
        "یه ساعت و ده دقیقه دیگه بخوابون => power sleep +70m",

        // ---- power, at a time ----
        "ساعت ۲ شب خاموش کن => power shutdown @02:00",
        "ساعت ۲ بامداد خاموش کن => power shutdown @02:00",
        "ساعت ۲ و نیم شب خاموش کن => power shutdown @02:30",
        "ساعت ۲:۳۰ شب خاموش کن => power shutdown @02:30",
        "ساعت ۱۱ شب خاموش کن => power shutdown @23:00",
        "ساعت ۱۱ و ربع شب خاموش کن => power shutdown @23:15",
        "ساعت ۲۳ خاموش کن => power shutdown @23:00",
        "ساعت ۲۳:۴۵ خاموش کن => power shutdown @23:45",
        "۲۳:۳۰ خاموش کن => power shutdown @23:30",
        "ساعت ۱۲ شب خاموش کن => power shutdown @00:00",
        "نصف شب خاموش کن => power shutdown @00:00",
        "نصفه شب خاموش کن => power shutdown @00:00",
        "ساعت ۱ نصف شب خاموش کن => power shutdown @01:00",
        "ساعت ۳ صبح خاموش کن => power shutdown @03:00",
        "ساعت ۱۰ صبح بخوابون => power sleep @10:00",
        "ساعت ۲ بعدازظهر خاموش کن => power shutdown @14:00",
        "ساعت ۲ بعد از ظهر خاموش کن => power shutdown @14:00",
        "ساعت ۵ عصر خاموش کن => power shutdown @17:00",
        "ساعت ۱ ظهر خاموش کن => power shutdown @13:00",
        "ساعت ۱۲ ظهر خاموش کن => power shutdown @12:00",
        "۲ بامداد خاموش کن => power shutdown @02:00",
        "۱۱ شب بخوابون => power sleep @23:00",
        "امشب ساعت ۱۱ خاموش کن => power shutdown @23:00",
        "امشب ساعت ۲ خاموش کن => power shutdown @02:00/1",
        "فردا ساعت ۸ صبح خاموش کن => power shutdown @08:00/1",
        "فردا صبح ساعت ۸ ری استارت کن => power restart @08:00/1",
        "فردا صبح خاموش کن => power shutdown @08:00/1",
        "فردا ظهر خاموش کن => power shutdown @12:00/1",
        "فردا ساعت ۱۴ خاموش کن => power shutdown @14:00/1",
        "فردا شب ساعت ۱۱ خاموش کن => power shutdown @23:00/1",
        "پس فردا ساعت ۱۰ صبح خاموش کن => power shutdown @10:00/2",
        "جمعه ساعت ۱۰ صبح خاموش کن => power shutdown @10:00/1",
        "شنبه ساعت ۹ صبح خاموش کن => power shutdown @09:00/2",
        "سه شنبه ساعت ۸ شب خاموش کن => power shutdown @20:00/5",
        "سه‌شنبه ساعت ۸ شب خاموش کن => power shutdown @20:00/5",
        "یه ربع به ۱۲ شب خاموش کن => power shutdown @23:45",
        "ربع به ۱ بامداد خاموش کن => power shutdown @00:45",
        "۱۰ دقیقه به ۱۱ شب خاموش کن => power shutdown @22:50",
        "ساعت ۲۲ بخوابون => power sleep @22:00",
        "ساعت ۰۲:۰۰ خاموش کن => power shutdown @02:00",
        "ساعت ۲۱:۳۰ خاموش کن => power shutdown @21:30",
        "حدود ساعت ۱۱ شب خاموش کن => power shutdown @23:00",
        "سر ساعت ۱۲ شب خاموش کن => power shutdown @00:00",

        // ---- weekly ----
        "هر شب ساعت ۱ خاموش کن => power shutdown @01:00 repeat=7",
        "هر شب ساعت ۱۲ خاموش کن => power shutdown @00:00 repeat=7",
        "هرشب ساعت ۲ خاموش کن => power shutdown @02:00 repeat=7",
        "هر روز ساعت ۲۳ خاموش کن => power shutdown @23:00 repeat=7",
        "هر جمعه ساعت ۱۰ صبح خاموش کن => power shutdown @10:00/1 repeat=1",
        "هر شنبه و سه شنبه ساعت ۱۱ شب خاموش کن => power shutdown @23:00/2 repeat=2",
        "هر شب خاموش کن => need",

        // ---- 12-hour ambiguity: ask ----
        "ساعت ۲ خاموش کن => clarify @02:00 @14:00",
        "ساعت ۸ خاموش کن => clarify @08:00 @20:00",
        "ساعت ۱۰ بخوابون => clarify @22:00 @10:00",
        "ساعت ۱۲ خاموش کن => clarify @00:00 @12:00",
        "ساعت ۲ و نیم خاموش کن => clarify @02:30 @14:30",
        "ساعت ۲:۳۰ خاموش کن => clarify @02:30 @14:30",
        "فردا ساعت ۸ خاموش کن => clarify @08:00/1 @20:00/1",
        "ساعت ۹ ری استارت کن => clarify @09:00 @21:00",

        // ---- conditions ----
        "وقتی دانلود تموم شد خاموش کن => rule network shutdown",
        "دانلود که تموم شد خاموش کن => rule network shutdown",
        "بعد از تموم شدن دانلود سیستم رو خاموش کن => rule network shutdown",
        "وقتی دانلودم تموم شد بخوابون => rule network sleep",
        "دانلود تموم شد خاموش کن => rule network shutdown",
        "بعد از دانلود خاموش => rule network shutdown",
        "دانلود استیم تموم شد خاموش کن => rule network shutdown",
        "۱۰ دقیقه بعد از تموم شدن دانلود خاموش کن => rule network shutdown hold=10",
        "وقتی دانلودا تموم شدن هایبرنیت کن => rule network hibernate",
        "وقتی آپلود تموم شد خاموش کن => rule upload shutdown",
        "اپلود که تموم شد بخوابون => rule upload sleep",
        "اگه ۲۰ دقیقه بیکار بود خاموش کن => rule idle shutdown hold=20",
        "اگه سیستم بیکار موند بخوابون => rule idle sleep hold=15",
        "اگه نیم ساعت کاری نکردم خاموش کن => rule idle shutdown hold=30",
        "اگه یه ساعت دست نزدم بخوابون => rule idle sleep hold=60",
        "وقتی پشت سیستم نبودم قفل کن => rule idle lock hold=15",
        "اگه ۱۰ دقیقه بی استفاده بود قفلش کن => rule idle lock hold=10",
        "وقتی steam بسته شد خاموش کن => rule process shutdown name=steam",
        "وقتی chrome بسته شد بخوابون => rule process sleep name=chrome",
        "وقتی استیم بسته شد خاموش کن => rule process shutdown name=steam",
        "وقتی blender.exe بسته شد خاموش کن => rule process shutdown name=blender",
        "وقتی بلندر بسته شد خاموش کن => rule process shutdown name=blender",
        "وقتی رندر تموم شد خاموش کن => rule load shutdown",
        "وقتی بازی تموم شد خاموش کن => need",
        "وقتی برنامه بسته شد خاموش کن => need",

        // ---- cancel ----
        "لغو کن => cancel",
        "لغو => cancel",
        "کنسل کن => cancel",
        "کنسلش کن => cancel",
        "خاموشی رو لغو کن => cancel",
        "لغو خاموشی => cancel",
        "بیخیال => cancel",
        "بیخیال خاموش نکن => cancel",
        "دستور خاموشی رو کنسل کن => cancel",
        "همه رو لغو کن => cancel",
        "متوقفش کن => cancel",
        "خاموش نکن => clarify cancel",
        "نخوابون => clarify cancel",

        // ---- wake ----
        "ساعت ۷ صبح بیدارش کن => wake @07:00/1",
        "فردا ساعت ۷ صبح سیستم رو بیدار کن => wake @07:00/1",
        "ساعت ۶:۳۰ صبح بیدار کن => wake @06:30/1",
        "هر روز ساعت ۷ صبح بیدار کن => wake @07:00/1 repeat=7",
        "بیدارش کن => need",
        "ساعت ۷ بیدار کن => clarify @07:00/1 @19:00/1",
        "سیستم رو ساعت ۸ صبح روشن کن => clarify wake",

        // ---- reminders ----
        "ساعت ۵ عصر یادم بنداز به مامان زنگ بزنم => remind @17:00/1 title=به مامان زنگ بزنم",
        "یادم بنداز ساعت ۵ عصر به مامان زنگ بزنم => remind @17:00/1 title=به مامان زنگ بزنم",
        "نیم ساعت دیگه یادم بنداز چای رو خاموش کنم => remind +30m title=چای رو خاموش کنم",
        "۱۰ دقیقه دیگه یادم بنداز قرص بخورم => remind +10m title=قرص بخورم",
        "فردا صبح یادم بنداز نون بخرم => remind @08:00/1 title=نون بخرم",
        "فردا ساعت ۹ صبح یادآوری کن جلسه دارم => remind @09:00/1 title=جلسه دارم",
        "یادم باشه فردا ساعت ۱۰ صبح برم بانک => remind @10:00/1 title=برم بانک",
        "یه ساعت دیگه یادم بنداز که سیستم رو خاموش کنم => remind +60m title=سیستم رو خاموش کنم",
        "یادآوری ساعت ۸ شب فیلم => remind @20:00/1 title=فیلم",
        "يادم بنداز ساعت ۱۱ شب دارو => remind @23:00 title=دارو",
        "یادم بنداز نون بخرم => need 3",
        "یادم بنداز => need",
        "یادم بنداز ساعت ۸ ورزش => clarify @08:00/1 @20:00/1",
        "جمعه ساعت ۱۰ صبح یادم بنداز گلدونا رو آب بدم => remind @10:00/1 title=گلدونا رو آب بدم",

        // ---- focus ----
        "تایمر تمرکز => focus 0",
        "تمرکز ۲۵ دقیقه => focus 25",
        "۲۵ دقیقه تمرکز => focus 25",
        "یه تمرکز ۵۰ دقیقه‌ای شروع کن => focus 50",
        "پومودورو شروع کن => focus 0",
        "فوکوس ۴۵ دقیقه => focus 45",
        "تمرکز رو متوقف کن => focus stop",
        "تمرکز رو تموم کن => focus stop",
        "تایمر تمرکز رو خاموش کن => focus stop",
        "تمرکز ۳ ساعت => need",

        // ---- night light ----
        "نور شب رو روشن کن => night on",
        "نور شب => night on",
        "نورشب روشن => night on",
        "نور شب رو خاموش کن => night off",
        "صفحه رو کم نور کن => night on",
        "صفحه رو کم‌نور کن => night on",
        "کم‌نورش کن ۴۰ درصد => night on 40",
        "نور شب ۶۰٪ => night on 60",
        "حالت شب رو فعال کن => night on",
        "حالت شب رو غیرفعال کن => night off",
        "نور شب رو بردار => night off",
        "نور شب رو لغو کن => night off",
        "صفحه رو تاریک کن => night on",
        "ساعت ۱۱ نور شب رو روشن کن => need",

        // ---- sleep mode ----
        "حالت شب‌خواب => sleepmode on 0",
        "شب‌خواب رو روشن کن => sleepmode on 0",
        "شبخواب ۴۵ دقیقه => sleepmode on 45",
        "شب خواب ۳۰ دقیقه => sleepmode on 30",
        "شب‌خواب رو لغو کن => sleepmode off",
        "شب‌خواب رو خاموش کن => sleepmode off",

        // ---- list ----
        "لیست دستورها => list",
        "چی تو صفه؟ => list",
        "صف دستورها رو نشون بده => list",
        "وضعیت => list",
        "زمان‌بندی‌ها => list",
        "کی خاموش میشه؟ => list",
        "سیستم کی خاموش میشه؟ => list",

        // ---- not understood / not a command ----
        "سلام => unknown",
        "هوا چطوره => unknown",
        "یه آهنگ پخش کن => unknown",
        "ساعت ۲ => need",
        "۳۰ دقیقه دیگه => need",
        "دیروز خاموش کردم و بعد رفتم بیرون با دوستام و خیلی خوش گذشت => clarify",
        "امروز ساعت ۸ صبح خاموش کن => need",

        // ---- statements and questions are never orders ----
        "خاموشی برق کی میاد => notready",
        "دیشب سیستم خودش خاموش شد چرا => notready",
        "صفحه خاموش شد => notready",
        "لپتاپم خاموش نمیشه => notready",
        "چرا سیستمم میخوابه => notready",
        "من خوابم میاد => notready",
        "خسته‌ام میخوام بخوابم => notready",
        "خاموش و روشن شد => notready",
        "سیستم هی ری استارت میشه => notready",
        "برق رفت => unknown",

        // ---- more everyday phrasings ----
        "سیستم رو یه ساعت دیگه خاموشش کن => power shutdown +60m",
        "لطفا ساعت ۱۲:۳۰ شب سیستمو خاموش کن => power shutdown @00:30",
        "ساعت دوازده و نیم شب خاموش کن => power shutdown @00:30",
        "ساعت یازده شب خاموش کن => power shutdown @23:00",
        "ساعت ده و ربع شب بخوابون => power sleep @22:15",
        "فردا ساعت هفت صبح بیدارم کن => wake @07:00/1",
        "بیست دقیقه دیگه بخوابه => power sleep +20m",
        "یکساعت دیگه خاموش کن => power shutdown +60m",
        "۲ساعت دیگه خاموش کن => power shutdown +120m",
        "تو ۱۰ دقیقه خاموش کن => power shutdown +10m",
        "تا نیم ساعت دیگه خاموش شو => power shutdown +30m",
        "یادم بنداز فردا ساعت ده صبح زنگ بزنم به علی => remind @10:00/1 title=زنگ بزنم به علی",
        "۲۰ دقیقه دیگه یادآوری کن غذا رو از روی گاز بردارم => remind +20m title=غذا رو از روی گاز بردارم",
        "وقتی دانلود فایلم تموم شد سیستم خاموش بشه => rule network shutdown",
        "اگه یه ربع بیکار موند خاموشش کن => rule idle shutdown hold=15",
        "وقتی IDM بسته شد خاموش کن => rule process shutdown name=IDMan",
        "وقتی دانلود تموم شد خاموش نکن => clarify cancel",
        "خاموش کن یا بخوابون => clarify",
        "یه ساعت دیگه => need 2",
        "بعدا خاموش کن => need 2",
        "خاموش کن ساعت ۲۵ => need",
        "ری استارت نکن => clarify cancel",
        "لغو همه دستورها => cancel",
        "تمرکز تا ساعت ۱۰ شب => focus 60",
        "شب‌خواب ۳۰ دقیقه با خاموشی => sleepmode on 30",
        "نور شب رو روشن کن ۳۰ درصد => night on 30",
        "خاموش کن، ممنون => power shutdown now",
        "سیستممو بخوابون لطفاً => power sleep now",
        "ريستارت كن => power restart now",
        "سيستم رو ساعت ۱۱ شب خاموش كن => power shutdown @23:00",
    ];

    public static readonly string[] English =
    [
        // ---- power, right now ----
        "shut down => power shutdown now",
        "shutdown => power shutdown now",
        "shut down the pc => power shutdown now",
        "shut it down => power shutdown now",
        "turn off the computer => power shutdown now",
        "turn the pc off => power shutdown now",
        "power off => power shutdown now",
        "switch off => power shutdown now",
        "shutdwon => power shutdown now",
        "Shut Down NOW => power shutdown now",
        "please shut down my laptop => power shutdown now",
        "sleep => power sleep now",
        "go to sleep => power sleep now",
        "put the pc to sleep => power sleep now",
        "sleeeep => power sleep now",
        "suspend => power sleep now",
        "hibernate => power hibernate now",
        "hibernate the computer => power hibernate now",
        "hybernate => power hibernate now",
        "restart => power restart now",
        "reboot => power restart now",
        "restart the pc please => power restart now",
        "resatrt => power restart now",
        "lock => power lock now",
        "lock the screen => power lock now",
        "lock my pc => power lock now",

        // ---- power, after a delay ----
        "shut down in 30 minutes => power shutdown +30m",
        "shut down in 30 min => power shutdown +30m",
        "shutdown in 30min => power shutdown +30m",
        "shut down in 30 mins => power shutdown +30m",
        "in 30 minutes shut down => power shutdown +30m",
        "shut down in an hour => power shutdown +60m",
        "shut down in 1 hour => power shutdown +60m",
        "shut down in 1h => power shutdown +60m",
        "shut down in 2 hours => power shutdown +120m",
        "shut down in an hour and a half => power shutdown +90m",
        "shut down in 1.5 hours => power shutdown +90m",
        "shut down in half an hour => power shutdown +30m",
        "sleep in half an hour => power sleep +30m",
        "sleep in 90 minutes => power sleep +90m",
        "shut down in 1h30m => power shutdown +90m",
        "shut down in 2 hours and 15 minutes => power shutdown +135m",
        "shut down in 1 hour 20 minutes => power shutdown +80m",
        "shut down in twenty minutes => power shutdown +20m",
        "shut down in twenty five minutes => power shutdown +25m",
        "shut down in a quarter of an hour => power shutdown +15m",
        "shut down after 45 minutes => power shutdown +45m",
        "shut down 10 minutes from now => power shutdown +10m",
        "turn off in 5 min => power shutdown +5m",
        "restart in 10 minutes => power restart +10m",
        "hibernate in 2 hours => power hibernate +120m",
        "lock in 5 minutes => power lock +5m",
        "power off in 40 minutes => power shutdown +40m",
        "shut down in 3 hrs => power shutdown +180m",
        "sleep in 30 seconds => power sleep +0.5m",
        "shut down 30 min => power shutdown +30m",

        // ---- power, at a time ----
        "shut down at 2am => power shutdown @02:00",
        "shut down at 2 am => power shutdown @02:00",
        "shut down at 2:30am => power shutdown @02:30",
        "shut down at 2:30 am => power shutdown @02:30",
        "shut down at 11pm => power shutdown @23:00",
        "shut down at 11:45 pm => power shutdown @23:45",
        "shut down at 23:00 => power shutdown @23:00",
        "shut down at 23:30 => power shutdown @23:30",
        "shut down at midnight => power shutdown @00:00",
        "shut down at noon => power shutdown @12:00/1",
        "sleep at 1 in the morning => power sleep @01:00",
        "shut down at 11 at night => power shutdown @23:00",
        "shut down at 2 at night => power shutdown @02:00",
        "shut down at 5 in the afternoon => power shutdown @17:00/1",
        "shut down at 7 in the evening => power shutdown @19:00/1",
        "shut down tonight at 11 => power shutdown @23:00",
        "shut down tonight at 1 => power shutdown @01:00/1",
        "shut down tomorrow at 8am => power shutdown @08:00/1",
        "restart tomorrow morning => power restart @08:00/1",
        "shut down tomorrow at 14:00 => power shutdown @14:00/1",
        "shut down on friday at 10am => power shutdown @10:00/1",
        "shut down saturday at 9 am => power shutdown @09:00/2",
        "shut down the day after tomorrow at 10am => power shutdown @10:00/2",
        "shut down at quarter to 12 => clarify @23:45 @11:45",
        "shut down at half past 11 pm => power shutdown @23:30",
        "shut down at quarter past 11 pm => power shutdown @23:15",
        "shut down at 11 o'clock pm => power shutdown @23:00",
        "sleep at 22:15 => power sleep @22:15",
        "shut down at 02:00 => power shutdown @02:00",
        "at 1am shut down => power shutdown @01:00",

        // ---- weekly ----
        "shut down every night at 1am => power shutdown @01:00 repeat=7",
        "shut down every day at 23:00 => power shutdown @23:00 repeat=7",
        "shut down daily at 11pm => power shutdown @23:00 repeat=7",
        "shut down every friday at 10pm => power shutdown @22:00/1 repeat=1",
        "shut down every monday and friday at 11pm => power shutdown @23:00/1 repeat=2",
        "shut down every night at 1 => power shutdown @01:00 repeat=7",
        "shut down every night => need",

        // ---- 12-hour ambiguity ----
        "shut down at 2 => clarify @02:00 @14:00",
        "shut down at 8 => clarify @08:00 @20:00",
        "shut down at 2:30 => clarify @02:30 @14:30",
        "shut down at 12 => clarify @00:00 @12:00",
        "sleep tomorrow at 7 => clarify @07:00/1 @19:00/1",

        // ---- conditions ----
        "shut down when the download finishes => rule network shutdown",
        "shut down after the download is done => rule network shutdown",
        "shut down when downloads complete => rule network shutdown",
        "sleep when download is done => rule network sleep",
        "shut down after download => rule network shutdown",
        "hibernate when the download completes => rule network hibernate",
        "shut down 10 minutes after the download finishes => rule network shutdown hold=10",
        "shut down when the upload finishes => rule upload shutdown",
        "shut down if idle for 20 minutes => rule idle shutdown hold=20",
        "sleep if I'm away for 30 min => rule idle sleep hold=30",
        "lock when idle => rule idle lock hold=15",
        "sleep after 1 hour of inactivity => rule idle sleep hold=60",
        "shut down when steam closes => rule process shutdown name=steam",
        "sleep when chrome is closed => rule process sleep name=chrome",
        "shut down when obs64.exe exits => rule process shutdown name=obs64",
        "shut down after blender closes => rule process shutdown name=blender",
        "shut down when the render is done => rule load shutdown",
        "shut down when the game ends => need",
        "shut down when it closes => need",

        // ---- cancel ----
        "cancel => cancel",
        "cancel shutdown => cancel",
        "cancel the shutdown => cancel",
        "cancel everything => cancel",
        "abort => cancel",
        "never mind => cancel",
        "stop => cancel",
        "stop the shutdown => cancel",
        "cancle => cancel",
        "don't shut down => clarify cancel",
        "do not sleep => clarify cancel",

        // ---- wake ----
        "wake the pc at 7am => wake @07:00/1",
        "wake up at 6:30 am => wake @06:30/1",
        "wake the computer tomorrow at 8am => wake @08:00/1",
        "wake every day at 7am => wake @07:00/1 repeat=7",
        "wake up => need",
        "wake at 7 => clarify @07:00/1 @19:00/1",
        "turn on the pc at 8am => clarify wake",

        // ---- reminders ----
        "remind me to call mom at 5pm => remind @17:00/1 title=call mom",
        "remind me at 5pm to call mom => remind @17:00/1 title=call mom",
        "remind me in 30 minutes to take out the trash => remind +30m title=take out the trash",
        "remind me in 10 min to check the oven => remind +10m title=check the oven",
        "remind me tomorrow morning to buy bread => remind @08:00/1 title=buy bread",
        "remind me tomorrow at 9am about the meeting => remind @09:00/1 title=meeting",
        "reminder at 8pm: movie night => remind @20:00/1 title=movie night",
        "remind me to stretch in an hour => remind +60m title=stretch",
        "remind me on friday at 10am to water the plants => remind @10:00/1 title=water the plants",
        "add task pay rent tomorrow at 9am => remind @09:00/1 title=pay rent",
        "remind me to buy milk => need 3",
        "remind me => need",
        "remind me at 8 to run => clarify @08:00/1 @20:00/1",
        "remind me to shut down the pc in an hour => remind +60m title=shut down the pc",

        // ---- focus ----
        "focus => focus 0",
        "start focus => focus 0",
        "focus for 25 minutes => focus 25",
        "focus 25 min => focus 25",
        "start a 50 minute focus => focus 50",
        "pomodoro => focus 0",
        "start pomodoro => focus 0",
        "stop focus => focus stop",
        "cancel the focus timer => focus stop",
        "end focus => focus stop",
        "focus for 3 hours => need",

        // ---- night light ----
        "night light => night on",
        "night light on => night on",
        "turn on night light => night on",
        "turn on the night light => night on",
        "turn off night light => night off",
        "turn the night light off => night off",
        "night light off => night off",
        "dim the screen => night on",
        "dim the screen 40% => night on 40",
        "dim to 70 percent => night on 70",
        "enable night mode => night on",
        "disable night mode => night off",
        "warm light => night on",
        "night light at 10pm => need",

        // ---- sleep mode ----
        "sleep mode => sleepmode on 0",
        "start sleep mode => sleepmode on 0",
        "sleep mode 45 minutes => sleepmode on 45",
        "bedtime => sleepmode on 0",
        "cancel sleep mode => sleepmode off",
        "stop sleep mode => sleepmode off",
        "turn off sleep mode => sleepmode off",

        // ---- list ----
        "what's scheduled => list",
        "list => list",
        "show the queue => list",
        "status => list",
        "list schedules => list",
        "what is planned => list",
        "when will the pc shut down? => clarify list",

        // ---- not understood ----
        "hello => unknown",
        "what's the weather => unknown",
        "play some music => unknown",
        "at 2am => need",
        "in 30 minutes => need",
        "shut down at 8am today => need",
        "shut down in 9 days => need",

        // ---- statements and questions are never orders ----
        "how do I shut down => notready",
        "the pc keeps going to sleep => notready",
        "why does my laptop restart => notready",
        "I want to sleep => notready",
        "my pc won't shut down => notready",
        "it already went to sleep => notready",

        // ---- more everyday phrasings ----
        "shut down in 2h => power shutdown +120m",
        "turn off pc after 45 min => power shutdown +45m",
        "sleep at 1:15am => power sleep @01:15",
        "shut down at 10 pm tonight => power shutdown @22:00",
        "shutdown tomorrow 8am => power shutdown @08:00/1",
        "can you shut down the pc in 20 minutes => power shutdown +20m",
        "could you put my computer to sleep => power sleep now",
        "shut down at eleven pm => power shutdown @23:00",
        "wake me at 7am => wake @07:00/1",
        "turn off night light in 10 minutes => need",
        "remind me in 2 hours to check the download => remind +120m title=check the download",
        "when idm closes shut down => rule process shutdown name=IDMan",
        "shut down when utorrent finishes => rule process shutdown name=utorrent",
        "sleep or hibernate => clarify",
        "shut down later => need 2",
        "shut down at 25:00 => need",
        "shut down in 0 minutes => power shutdown now",
        "cancel all => cancel",
        "stop everything => cancel",
        "don't restart => clarify cancel",
        "focus until 10pm => focus 60",
        "night light 200% => night on 92",
        "sleep mode with shutdown in 30 minutes => sleepmode on 30",
        "SHUTDOWN IN 15 MINUTES => power shutdown +15m",
        "shut down in fifteen mins pls => power shutdown +15m",
        "put it to sleep at 23:59 => power sleep @23:59",
    ];

    public static IEnumerable<object[]> PersianCases => Persian.Select(c => new object[] { c });
    public static IEnumerable<object[]> EnglishCases => English.Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(PersianCases))]
    public void Persian_sentences(string line) => Check(line);

    [Theory]
    [MemberData(nameof(EnglishCases))]
    public void English_sentences(string line) => Check(line);

    [Fact]
    public void There_are_a_few_hundred_samples() => Assert.True(Persian.Length + English.Length >= 400, $"{Persian.Length + English.Length} samples");

    /// <summary>
    /// Expectations:
    /// <c>power ACTION now|+Nm|@HH:mm[/D]</c> (Ready; @ without /D = the next time the clock reads that, /D = days from today),
    /// <c>rule network|upload|idle|process|load ACTION [hold=N] [name=X]</c>, <c>cancel</c>, <c>wake @…</c>,
    /// <c>remind +Nm|@… title=…</c>, <c>focus N|stop</c>, <c>night on|off [N]</c>, <c>sleepmode on N|off</c>, <c>list</c>,
    /// <c>clarify [@… @…] [cancel|list|wake]</c> (choices), <c>need [N choices]</c>, <c>unknown</c>, <c>notready</c> (anything but a command); <c>repeat=N</c> counts weekly days.
    /// </summary>
    private static void Check(string line)
    {
        var parts = line.Split(" => ");
        var text = parts[0];
        var spec = parts[1];
        var r = CommandParser.Parse(text, Now);
        var words = spec.Split(' ');
        var title = spec.Contains("title=") ? spec[(spec.IndexOf("title=", StringComparison.Ordinal) + 6)..] : null;
        if (title is not null) words = spec[..spec.IndexOf("title=", StringComparison.Ordinal)].Trim().Split(' ');
        var why = $"«{text}» → {r.Status} {Describe(r)}";

        switch (words[0])
        {
            case "unknown":
                Assert.True(r.Status == CommandStatus.Unknown, why);
                return;
            case "notready":
                Assert.True(r.Status != CommandStatus.Ready && r.Command is null, why);
                Assert.DoesNotContain(r.Choices, c => c.Intent == CommandIntent.Power && c.At is null && r.Choices.Count == 1);
                return;
            case "need":
                Assert.True(r.Status == CommandStatus.NeedsInfo, why);
                Assert.True(r.Question.Length > 0, why);
                if (words.Length > 1) Assert.True(r.Choices.Count == int.Parse(words[1]), why);
                return;
            case "clarify":
                Assert.True(r.Status == CommandStatus.Clarify, why);
                Assert.True(r.Choices.Count > 0 || words.Length == 1, why);
                var moments = words.Skip(1).Where(w => w.StartsWith('@')).Select(At).ToList();
                if (moments.Count > 0) Assert.True(moments.SequenceEqual(r.Choices.Select(c => c.At!.Value)), why);
                foreach (var w in words.Skip(1).Where(w => !w.StartsWith('@')))
                {
                    var intent = w switch { "cancel" => CommandIntent.Cancel, "list" => CommandIntent.ListSchedules, "wake" => CommandIntent.Wake, _ => throw new ArgumentException(w) };
                    Assert.True(r.Choices.Any(c => c.Intent == intent), why);
                }
                return;
        }

        Assert.True(r.Status == CommandStatus.Ready, why);
        var cmd = r.Command!;
        Assert.True(r.Confidence >= CommandParser.ReadyAt, why);
        Assert.True(cmd.Summary(Now).Length > 0, why);
        var repeat = words.FirstOrDefault(w => w.StartsWith("repeat=", StringComparison.Ordinal));
        Assert.True(cmd.Repeat.Count == (repeat is null ? cmd.Intent == CommandIntent.Wake ? 1 : 0 : int.Parse(repeat[7..])), why);
        switch (words[0])
        {
            case "power":
                Assert.True(cmd.Intent == CommandIntent.Power && cmd.Condition is null, why);
                Assert.True(cmd.Action == Action(words[1]), why);
                AssertWhen(cmd.At, words[2], why);
                break;
            case "rule":
                Assert.True(cmd.Intent == CommandIntent.Power && cmd.Condition is not null, why);
                var rule = cmd.Condition!;
                var kind = words[1] switch { "network" or "upload" => RuleKind.Network, "idle" => RuleKind.Idle, "process" => RuleKind.ProcessExit, _ => RuleKind.Load };
                Assert.True(rule.Kind == kind && rule.Upload == (words[1] == "upload"), why);
                Assert.True(rule.Action == Action(words[2]) && cmd.Action == rule.Action, why);
                foreach (var w in words.Skip(3))
                {
                    if (w.StartsWith("hold=", StringComparison.Ordinal)) Assert.True(rule.HoldMinutes == int.Parse(w[5..]), why);
                    if (w.StartsWith("name=", StringComparison.Ordinal)) Assert.True(rule.ProcessName == w[5..], why);
                }
                break;
            case "cancel":
                Assert.True(cmd.Intent == CommandIntent.Cancel, why);
                break;
            case "wake":
                Assert.True(cmd.Intent == CommandIntent.Wake, why);
                AssertWhen(cmd.At, words[1], why);
                break;
            case "remind":
                Assert.True(cmd.Intent == CommandIntent.Reminder, why);
                AssertWhen(cmd.At, words[1], why);
                Assert.True(cmd.Title == title, why);
                break;
            case "focus":
                Assert.True(cmd.Intent == CommandIntent.Focus, why);
                if (words[1] == "stop") Assert.True(!cmd.On, why);
                else Assert.True(cmd.On && cmd.Minutes == int.Parse(words[1]), why);
                break;
            case "night":
                Assert.True(cmd.Intent == CommandIntent.NightLight && cmd.On == (words[1] == "on"), why);
                Assert.True(cmd.Level == (words.Length > 2 ? int.Parse(words[2]) : 0), why);
                break;
            case "sleepmode":
                Assert.True(cmd.Intent == CommandIntent.SleepMode && cmd.On == (words[1] == "on"), why);
                if (words.Length > 2) Assert.True(cmd.Minutes == int.Parse(words[2]), why);
                break;
            case "list":
                Assert.True(cmd.Intent == CommandIntent.ListSchedules, why);
                break;
            default:
                throw new ArgumentException(spec);
        }
    }

    private static PowerAction Action(string name) => Enum.Parse<PowerAction>(name, ignoreCase: true);

    private static DateTime At(string w)
    {
        var body = w[1..];
        var slash = body.IndexOf('/');
        var clock = TimeSpan.ParseExact(slash < 0 ? body : body[..slash], "hh\\:mm", CultureInfo.InvariantCulture);
        return slash < 0 ? Schedule.Next(clock.Hours, clock.Minutes, [], Now) : Now.Date.AddDays(int.Parse(body[(slash + 1)..])) + clock;
    }

    private static void AssertWhen(DateTime? at, string w, string why)
    {
        if (w == "now") Assert.True(at is null, why);
        else if (w.StartsWith('+')) Assert.True(at == Now.AddMinutes(double.Parse(w[1..^1], CultureInfo.InvariantCulture)), why);
        else Assert.True(at == At(w), why);
    }

    private static string Describe(CommandResult r)
    {
        static string One(AssistantCommand c) =>
            $"{c.Intent} {c.Action} {c.At:yyyy-MM-dd HH:mm} rep={c.Repeat.Count} on={c.On} min={c.Minutes} lvl={c.Level} title=«{c.Title}» rule={c.Condition?.Kind}/{c.Condition?.Action}/{c.Condition?.HoldMinutes}/{c.Condition?.ProcessName}";
        return (r.Command is { } cmd ? One(cmd) : "") + string.Concat(r.Choices.Select(c => " | " + One(c))) + $" conf={r.Confidence:0.00} q={r.Question}";
    }

    // ---- beyond single sentences ---------------------------------------------------------

    [Fact]
    public void Normalizing_unifies_arabic_letters_digits_and_the_zero_width_joiner()
    {
        Assert.Equal("سیستم 123 میخوام", CommandText.Normalize("سيستم ١٢٣ می‌خوام"));
        Assert.Equal(CommandText.Tokenize("می خوام").Single().Norm, CommandText.Tokenize("می‌خوام").Single().Norm);
        Assert.Equal(CommandText.Tokenize("میخوام").Single().Norm, CommandText.Tokenize("می‌خوام").Single().Norm);
        Assert.Equal("خاموش", CommandText.Normalize("خامووووش"));
        Assert.Equal("sleep", CommandText.Normalize("SLEEEEP"));
        Assert.Equal(["2", "pm"], CommandText.Tokenize("2pm").Select(t => t.Norm));
        Assert.Equal(["2:30"], CommandText.Tokenize("۲:۳۰").Select(t => t.Norm));
        Assert.Equal(["shut", "down"], CommandText.Tokenize("shut down.").Select(t => t.Norm));
    }

    [Fact]
    public void Huge_or_empty_input_never_throws_and_runs_nothing()
    {
        Assert.Equal(CommandStatus.Unknown, CommandParser.Parse(null, Now).Status);
        Assert.Equal(CommandStatus.Unknown, CommandParser.Parse("   ", Now).Status);
        var huge = CommandParser.Parse(string.Concat(Enumerable.Repeat("خاموش کن ", 5000)), Now);
        Assert.NotNull(huge);
        var junk = CommandParser.Parse("\u0000‌‏؟؟؟!!!", Now);
        Assert.Null(junk.Command);
    }

    [Fact]
    public void Low_confidence_never_returns_a_command()
    {
        foreach (var line in Persian.Concat(English))
        {
            var r = CommandParser.Parse(line.Split(" => ")[0], Now);
            if (r.Status != CommandStatus.Ready) Assert.Null(r.Command);
            else Assert.True(r.Confidence >= CommandParser.ReadyAt);
        }
    }

    [Fact]
    public void A_typo_costs_confidence()
    {
        var exact = CommandParser.Parse("shutdown", Now);
        var typo = CommandParser.Parse("shutdwon", Now);
        Assert.True(typo.Confidence < exact.Confidence);
    }

    [Fact]
    public void Times_in_the_past_or_too_far_ahead_are_refused()
    {
        Assert.Equal(CommandStatus.NeedsInfo, CommandParser.Parse("امروز ساعت ۸ صبح خاموش کن", Now).Status);
        Assert.Equal(CommandStatus.NeedsInfo, CommandParser.Parse("shut down in 8 days", Now).Status);
        Assert.Equal(CommandStatus.Ready, CommandParser.Parse("shut down in 6 days", Now).Status);
    }

    [Fact]
    public void The_bot_request_uses_the_existing_protocol()
    {
        var later = CommandParser.Parse("۳۰ دقیقه دیگه خاموش کن", Now).Command!.ToBotRequest(Now)!.Value;
        Assert.Equal("power", later.Type);
        using (var doc = JsonDocument.Parse(later.Args))
        {
            Assert.Equal("shutdown", doc.RootElement.GetProperty("action").GetString());
            Assert.Equal(1800, doc.RootElement.GetProperty("delaySeconds").GetInt32());
        }
        Assert.Equal("cancel", CommandParser.Parse("لغو", Now).Command!.ToBotRequest(Now)!.Value.Type);
        Assert.Equal("dim", CommandParser.Parse("نور شب رو خاموش کن", Now).Command!.ToBotRequest(Now)!.Value.Type);
        Assert.Equal("focus", CommandParser.Parse("stop focus", Now).Command!.ToBotRequest(Now)!.Value.Type);
        Assert.Equal("sleep-cancel", CommandParser.Parse("cancel sleep mode", Now).Command!.ToBotRequest(Now)!.Value.Type);
        Assert.Equal("status", CommandParser.Parse("وضعیت", Now).Command!.ToBotRequest(Now)!.Value.Type);
        // no bot command for these: the PC handles them itself
        Assert.Null(CommandParser.Parse("وقتی دانلود تموم شد خاموش کن", Now).Command!.ToBotRequest(Now));
        Assert.Null(CommandParser.Parse("remind me to call mom at 5pm", Now).Command!.ToBotRequest(Now));
    }

    [Fact]
    public void The_summary_names_the_exact_time()
    {
        var was = L.En;
        try
        {
            L.En = true;
            var at = CommandParser.Parse("shut down at 2:30am", Now).Command!;
            Assert.Contains("02:30", at.Summary(Now));
            Assert.Contains("Tomorrow", at.Summary(Now));
            L.En = false;
            Assert.Contains("۰۲:۳۰", at.Summary(Now));
            Assert.Contains("فردا", at.Summary(Now));
        }
        finally { L.En = was; }
    }
}
