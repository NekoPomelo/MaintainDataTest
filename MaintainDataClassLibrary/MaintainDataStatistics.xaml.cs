using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace MaintainDataClassLibrary
{
    /// <summary>
    /// MaintainDataStatistics.xaml 的交互逻辑 维护数据统计类
    /// </summary>
    public partial class MaintainDataStatistics : UserControl
    {
        public MaintainDataStatistics()
        {
            InitializeComponent();
            //MVVM：视图只负责装配视图模型，不做任何数据计算
            VM = new MaintainDataStatisticsVM();
            DataContext = VM;
        }

        /// <summary>
        /// 该页面的视图模型，供外部（如主窗口刷新）访问
        /// </summary>
        public MaintainDataStatisticsVM VM { get; }
    }
    /// <summary>
    /// 柱状图内容选项
    /// </summary>
    public enum ChartContent
    {
        MTBF,
        MTBA,
        MTTA,
    }

    /// <summary>
    /// 柱状图的一根柱子（MVVM的Model，视图只做数据绑定不做计算）
    /// </summary>
    public class ChartBarItem
    {
        public DateTime Date { get; set; }
        public string Label { get; set; } = string.Empty;
        public double Value { get; set; }
        public double Height { get; set; }
        public bool HasValue { get; set; }
        public string ValueText { get; set; } = string.Empty;
        public string Tip { get; set; } = string.Empty;
    }

    /// <summary>
    /// MaintainDataStatistics页面的视图模型
    /// 显示MTBF/MTBA/MTTA三项指标，并按用户选择的时间区间与内容绘制柱状图
    /// </summary>
    public class MaintainDataStatisticsVM : INotifyPropertyChanged
    {
        //柱状图绘图区高度（像素），与视图中的CKBarArea高度保持一致
        public const double ChartHeight = 300d;

        public MaintainDataStatisticsVM()
        {
            Bars = new ObservableCollection<ChartBarItem>();

            ////首次使用先准备一段演示数据，保证界面有内容可展示
            //try
            //{
            //    var data = MaintainDataStatisticsData.Instent;
            //    data.ConnectDatabase();
            //    if (data.GetLastRecord() == null)
            //    {
            //        data.SeedDemoData(10);
            //    }
            //}
            //catch (Exception)
            //{
            //    //数据库不可用时不应导致界面崩溃，界面显示空数据即可
            //}

            SelectContentCommand = new ClickICommand<string>(SelectContent);
            RefreshCommand = new ClickICommand(Refresh);

            var today = DateTime.Today;
            _startDate = today.AddDays(-13);
            _endDate = today;
            Refresh();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        MaintainDataStatisticsData Data => MaintainDataStatisticsData.Instent;

        #region 顶部三项指标
        /// <summary>MTBF：平均故障间隔时间，单位小时</summary>
        public double MTBF { get; private set; }
        /// <summary>MTBA：平均间隔辅助时间，单位小时</summary>
        public double MTBA { get; private set; }
        /// <summary>MTTA：平均协助时间，单位分钟</summary>
        public double MTTA { get; private set; }

        public string MTBFText => MTBF > 0 ? $"{MTBF:0.00} h" : "--";
        public string MTBAText => MTBA > 0 ? $"{MTBA:0.00} h" : "--";
        public string MTTAText => MTTA > 0 ? $"{MTTA:0.00} min" : "--";

        //指标目标：MTBF>1000h，MTBA>120h，MTTA<15min
        public bool MTBFGood => MTBF > 1000d;
        public bool MTBAGood => MTBA > 120d;
        public bool MTTAGood => MTTA > 0 && MTTA < 15d;

        public string MTBFThreshold => "目标 > 1000 h";
        public string MTBAThreshold => "目标 > 120 h";
        public string MTTAThreshold => "目标 < 15 min";

        /// <summary>累计维修次数、协助次数、累计运行时间说明</summary>
        public string SummaryText { get; private set; } = string.Empty;
        #endregion

        #region 图表数据
        public ObservableCollection<ChartBarItem> Bars { get; }

        public double ChartBarAreaHeight => ChartHeight;
        public double ChartColumnWidth => 48d;

        double _axisMax = 1d;
        public string YAxisTopText { get; private set; } = "0";
        public string YAxisMiddleText { get; private set; } = "0";
        public string YAxisBottomText => "0";
        public string YAxisUnitText { get; private set; } = "单位：小时";
        #endregion

        #region 区间与内容选择
        DateTime _startDate;
        public DateTime StartDate
        {
            get => _startDate;
            set
            {
                if (_startDate == value) return;
                _startDate = value;
                OnPropertyChanged();
                Refresh();
            }
        }

        DateTime _endDate;
        public DateTime EndDate
        {
            get => _endDate;
            set
            {
                if (_endDate == value) return;
                _endDate = value;
                OnPropertyChanged();
                Refresh();
            }
        }

        ChartContent _content = ChartContent.MTBF;
        public ChartContent Content
        {
            get => _content;
            set
            {
                if (_content == value) return;
                _content = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsMTBF));
                OnPropertyChanged(nameof(IsMTBA));
                OnPropertyChanged(nameof(IsMTTA));
                OnPropertyChanged(nameof(ContentText));
                Refresh();
            }
        }

        public bool IsMTBF => Content == ChartContent.MTBF;
        public bool IsMTBA => Content == ChartContent.MTBA;
        public bool IsMTTA => Content == ChartContent.MTTA;

        public string ContentText => Content switch
        {
            ChartContent.MTBA => "MTBA（平均间隔辅助时间）",
            ChartContent.MTTA => "MTTA（平均协助时间）",
            _ => "MTBF（平均故障间隔时间）",
        };

        /// <summary>内容选项按钮：MTBF / MTBA / MTTA</summary>
        public ClickICommand<string> SelectContentCommand { get; }

        /// <summary>手动刷新按钮（设备运行过程中点击可看到最新数据）</summary>
        public ClickICommand RefreshCommand { get; }

        void SelectContent(string? content)
        {
            if (Enum.TryParse<ChartContent>(content, out var parsed))
            {
                Content = parsed;
            }
        }
        #endregion

        #region 数据处理
        /// <summary>
        /// 按当前的时间区间与内容选项重新计算指标与柱状图
        /// </summary>
        public void Refresh()
        {
            DateTime start = StartDate.Date;
            DateTime end = EndDate.Date;
            if (end < start)
            {
                //结束时间不允许早于开始时间
                end = start;
                if (_endDate != end)
                {
                    _endDate = end;
                    OnPropertyChanged(nameof(EndDate));
                }
            }

            List<DateStatisticRow> rows;
            SummaryRow summary;
            DateStatisticRow? baseRow;
            try
            {
                rows = Data.GetDailyStatistics(start, end);
                summary = Data.GetSummary();
                baseRow = Data.GetBeforeOfDay(start);
            }
            catch (Exception)
            {
                rows = new List<DateStatisticRow>();
                summary = new SummaryRow();
                baseRow = null;
            }

            //区间内（含区间之前）的累计值 = 区间起始日"当日之前"的累计 + 区间内每日增量
            double runningHours = baseRow?.BeforeRunningTime ?? 0d;
            double assistanceHours = baseRow?.BeforeAssistanceTime ?? 0d;
            int repairCount = baseRow?.BeforeNumberOfRepairs ?? 0;
            int assistanceCount = baseRow?.BeforeNumberOfAssistance ?? 0;

            var lookup = rows.ToDictionary(r => r.Date.Date);
            var bars = new List<ChartBarItem>();
            double maxValue = 0d;

            for (DateTime day = start; day <= end; day = day.AddDays(1))
            {
                double value = 0d;
                if (lookup.TryGetValue(day, out var dayRow))
                {
                    runningHours += dayRow.DayRunningTime;
                    assistanceHours += dayRow.DayAssistanceTime;
                    repairCount += dayRow.DayNumberOfRepairs;
                    assistanceCount += dayRow.DayNumberOfAssistance;
                    //在某一天的柱子上直接按当日数据计算当前内容选项的数值，避免逐日查询数据库
                    value = ValueOf(dayRow);
                }
                if (value > maxValue) maxValue = value;
                bars.Add(new ChartBarItem
                {
                    Date = day,
                    Label = day.ToString("MM-dd", CultureInfo.InvariantCulture),
                    Value = value,
                    HasValue = value > 0d,
                    ValueText = FormatChartValue(value, Content),
                    Tip = BuildToolTip(day, value),
                });
            }

            MTBF = MaintainDataStatisticsData.Mtbf(runningHours, repairCount);
            MTBA = MaintainDataStatisticsData.Mtba(runningHours, assistanceCount);
            MTTA = MaintainDataStatisticsData.Mtta(assistanceHours, assistanceCount);

            //Y轴刻度取"好看"的整数值，避免刻度出现长小数
            _axisMax = NiceAxisMax(maxValue);
            YAxisUnitText = Content == ChartContent.MTTA ? "单位：分钟" : "单位：小时";
            YAxisTopText = FormatAxisValue(_axisMax);
            YAxisMiddleText = FormatAxisValue(_axisMax / 2d);

            double scale = _axisMax > 0d ? ChartHeight / _axisMax : 0d;
            foreach (var bar in bars)
            {
                bar.Height = Math.Max(0d, Math.Min(ChartHeight, bar.Value * scale));
            }

            Bars.Clear();
            foreach (var bar in bars)
            {
                Bars.Add(bar);
            }

            SummaryText = $"累计运行 {MaintainDataStatisticsData.FormatHours(summary.TotalRunningTime)}   " +
                          $"累计协助 {MaintainDataStatisticsData.FormatHours(summary.TotalAssistanceTime)}   " +
                          $"维修 {summary.TotalNumberOfRepairs} 次   协助 {summary.TotalNumberOfAssistance} 次";

            OnPropertyChanged(nameof(MTBF));
            OnPropertyChanged(nameof(MTBA));
            OnPropertyChanged(nameof(MTTA));
            OnPropertyChanged(nameof(MTBFText));
            OnPropertyChanged(nameof(MTBAText));
            OnPropertyChanged(nameof(MTTAText));
            OnPropertyChanged(nameof(MTBFGood));
            OnPropertyChanged(nameof(MTBAGood));
            OnPropertyChanged(nameof(MTTAGood));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(ContentText));
            OnPropertyChanged(nameof(YAxisTopText));
            OnPropertyChanged(nameof(YAxisMiddleText));
            OnPropertyChanged(nameof(YAxisUnitText));
            OnPropertyChanged(nameof(Bars));
        }

        /// <summary>
        /// 取某一天当前内容选项对应的数值（按当日数据计算）
        /// </summary>
        double ValueOf(DateStatisticRow row) => Content switch
        {
            ChartContent.MTBA => MaintainDataStatisticsData.DayMtba(row),
            ChartContent.MTTA => MaintainDataStatisticsData.DayMtta(row),
            _ => MaintainDataStatisticsData.DayMtbf(row),
        };

        string BuildToolTip(DateTime day, double value)
        {
            if (value <= 0d)
            {
                return $"{day:yyyy-MM-dd}\n{Content}：无数据";
            }
            string suffix = Content == ChartContent.MTTA ? "分钟" : "小时";
            return $"{day:yyyy-MM-dd}\n{Content}：{value:0.00} {suffix}";
        }

        static string FormatChartValue(double value, ChartContent content)
            => value <= 0d ? "--" : value.ToString("0.##", CultureInfo.InvariantCulture);

        static string FormatAxisValue(double value)
            => value >= 100d ? value.ToString("0", CultureInfo.InvariantCulture)
                             : value.ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>
        /// 计算Y轴最大值：向上取整到1/2/5×10^n，使刻度为整数
        /// </summary>
        internal static double NiceAxisMax(double max)
        {
            if (max <= 0d) return 1d;
            double exponent = Math.Floor(Math.Log10(max));
            double power = Math.Pow(10d, exponent);
            double fraction = max / power;
            double nice = fraction <= 1d ? 1d : fraction <= 2d ? 2d : fraction <= 5d ? 5d : 10d;
            return nice * power;
        }
        #endregion
    }
   
}
