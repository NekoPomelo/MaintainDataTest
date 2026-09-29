using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace MaintainDataClassLibrary
{
    /// <summary>
    /// MaintainWindow.xaml 的交互逻辑,维护窗口
    /// </summary>
    public partial class MaintainWindow : Window
    {
        public MaintainWindow()
        {
            InitializeComponent();
            //MVVM：视图只负责装配视图模型
            DataContext = new MaintainWindowVM(this);
        }
    }

    /// <summary>
    /// 设备当前所处的状态（由时间戳表最后一条记录推导，不额外占用存储）
    /// </summary>
    public enum DeviceState
    {
        None,//无记录，从未开始统计
        Running,//正常运行
        Repairing,//维修中
        Assisting,//协助中
    }

    public class MaintainWindowVM : INotifyPropertyChanged
    {
        readonly Window? _window;

        public MaintainWindowVM() : this(null) { }

        public MaintainWindowVM(Window? window)
        {
            _window = window;
            ButtonClick = new ClickICommand<string>(Click);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName]string? propertyName=null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #region 按钮显示状态（由设备当前状态决定，互斥显示，避免出现非法状态跳转）
        /// <summary>
        /// 设备当前状态，取时间戳表最后一条记录的类型与结束时间
        /// </summary>
        public DeviceState CurrentState
        {
            get
            {
                var last = MaintainDataStatisticsData.Instent.GetLastRecord();
                if (last == null) return DeviceState.None;
                //有结束时间说明上一段已经结束，设备处于"待机/未记录"状态
                if (!string.IsNullOrEmpty(last.Value.EndTime)) return DeviceState.None;
                return last.Value.TimestampType switch
                {
                    nameof(RecordType.RunningTime) => DeviceState.Running,
                    nameof(RecordType.RepairTime) => DeviceState.Repairing,
                    nameof(RecordType.AssistanceTime) => DeviceState.Assisting,
                    _ => DeviceState.None,
                };
            }
        }

        Visibility VisibleIf(bool condition) => condition ? Visibility.Visible : Visibility.Collapsed;

        //无"进行中的维修"时才能开始维修且不在进行协助中
        public Visibility RepairStartVisibility => VisibleIf(CurrentState != DeviceState.Repairing&& CurrentState!=DeviceState.Assisting);
        //维修中才能结束维修
        public Visibility RepairEndVisibility => VisibleIf(CurrentState == DeviceState.Repairing);
        //无"进行中的协助"时才能开始协助且不在维修中
        public Visibility AssistanceStartVisibility => VisibleIf(CurrentState != DeviceState.Assisting && CurrentState != DeviceState.Repairing);
        //协助中才能结束协助
        public Visibility AssistanceEndVisibility => VisibleIf(CurrentState == DeviceState.Assisting);
        //处于运行状态才允许临时暂停
        public Visibility PauseVisibility => VisibleIf(CurrentState == DeviceState.Running);
        //取消始终可用
        public Visibility CancelVisibility => Visibility.Visible;
        #endregion

        /// <summary>
        /// 通知界面重新计算各按钮的可见性
        /// </summary>
        public void RefreshVisibility()
        {
            OnPropertyChanged(nameof(CurrentState));
            OnPropertyChanged(nameof(RepairStartVisibility));
            OnPropertyChanged(nameof(RepairEndVisibility));
            OnPropertyChanged(nameof(AssistanceStartVisibility));
            OnPropertyChanged(nameof(AssistanceEndVisibility));
            OnPropertyChanged(nameof(PauseVisibility));
            OnPropertyChanged(nameof(CancelVisibility));
        }

        public ClickICommand<string> ButtonClick { get; set; }

        public void Click(string Parameter)
        {
            switch (Parameter)
            {
                case "RepairStart":
                    //开始维修：结束上一段（正常运行/协助），开启维修日志
                    StartCare(nameof(RecordType.RepairTime));
                    break;
                case "RepairEnd":
                    //结束维修：结束维修日志，恢复正常运行
                    EndCare();
                    break;
                case "AssistanceStart":
                    //开始协助：结束上一段（正常运行/维修），开启协助日志
                    StartCare(nameof(RecordType.AssistanceTime));
                    break;
                case "AssistanceEnd":
                    //结束协助：结束协助日志，恢复正常运行
                    EndCare();
                    break;
                case "Pause":
                    //临时性暂停：只结束当前日志，不开启新日志
                    PauseOrDown();
                    break;
                case "Cancel":
                    //取消：关闭窗口
                    _window?.Close();
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// 开始维修/协助：关闭上一段日志，写入以当前时间为开始时间的维修/协助日志
        /// </summary>
        void StartCare(string recordType)
        {
            var data = MaintainDataStatisticsData.Instent;
            if (CurrentState == DeviceState.Running)
            {
                //设备处于运行状态，先结束运行日志
                data.CloseOpenRecord(DateTime.Now);
            }
            data.AppendRecord(recordType, DateTime.Now);
            RefreshVisibility();
        }

        /// <summary>
        /// 结束维修/协助：结束对应日志，并重新开始一段运行日志
        /// 遵循"一段日志的结束就是下一段日志的开始"
        /// </summary>
        void EndCare()
        {
            var data = MaintainDataStatisticsData.Instent;
            if (CurrentState == DeviceState.None) return;
            data.CloseOpenRecord(DateTime.Now);
            //data.AppendRecord(nameof(RecordType.RunningTime), DateTime.Now);
            RefreshVisibility();
        }

        /// <summary>
        /// 自动运行时触发
        /// 
        /// </summary>
        public static void StartRun()
        {
            //查时间戳表，查看最近的时间戳表中最近的一条记录是否有结束时间
            //最近的一条记录如没有结束时间则将当前时间作为结束时间写入
            var data = MaintainDataStatisticsData.Instent;
            data.CloseOpenRecord(DateTime.Now);

            //最后向表中额外增加一条RunningTime日志记录，当前时间作为开始时间写入日志中
            data.AppendRecord(nameof(RecordType.RunningTime), DateTime.Now);
        }

        /// <summary>
        /// 暂停或Down机或异常停止时触发
        /// 设备不再运行，结束当前处于打开状态的日志，本次统计结束
        /// </summary>
        public static void PauseOrDown()
        {
            MaintainDataStatisticsData.Instent.CloseOpenRecord(DateTime.Now);
        }

        /// <summary>
        /// 手动触发（取消维修/协助等人工干预后，回到正常运行状态）
        /// </summary>
        public static void Manual()
        {
            var data = MaintainDataStatisticsData.Instent;
            data.CloseOpenRecord(DateTime.Now);
            data.AppendRecord(nameof(RecordType.RunningTime), DateTime.Now);
        }
    }
}
