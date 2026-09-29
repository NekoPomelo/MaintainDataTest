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
using MaintainDataClassLibrary;

namespace TestWPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            //数据库连接与建表
            try
            {
                MaintainDataStatisticsData.Instent.ConnectDatabase();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"数据库初始化失败：{ex.Message}", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 打开维护窗口（与设备实际运行时弹出的维护窗口一致）
        /// </summary>
        private void OpenMaintainWindow_Click(object sender, RoutedEventArgs e)
        {
            var window = new MaintainWindow { Owner = this };
            window.ShowDialog();
            StatisticsPage.VM.Refresh();
        }

        /// <summary>
        /// 设备开始自动运行时由设备程序调用 MaintainWindowVM.StartRun
        /// 此处用于模拟设备状态变化
        /// </summary>
        void AfterStateChanged() => StatisticsPage.VM.Refresh();

        private void SimulateStartRun_Click(object sender, RoutedEventArgs e)
        {
            MaintainWindowVM.StartRun();
            AfterStateChanged();
        }

        private void SimulateRepairStart_Click(object sender, RoutedEventArgs e)
        {
            new MaintainWindowVM().Click("RepairStart");
            AfterStateChanged();
        }

        private void SimulateRepairEnd_Click(object sender, RoutedEventArgs e)
        {
            new MaintainWindowVM().Click("RepairEnd");
            AfterStateChanged();
        }

        private void SimulateAssistanceStart_Click(object sender, RoutedEventArgs e)
        {
            new MaintainWindowVM().Click("AssistanceStart");
            AfterStateChanged();
        }

        private void SimulateAssistanceEnd_Click(object sender, RoutedEventArgs e)
        {
            new MaintainWindowVM().Click("AssistanceEnd");
            AfterStateChanged();
        }

        private void SimulatePause_Click(object sender, RoutedEventArgs e)
        {
            MaintainWindowVM.PauseOrDown();
            AfterStateChanged();
        }

        private void SeedDemoData_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MaintainDataStatisticsData.Instent.SeedDemoData(10);
                StatisticsPage.VM.Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"生成演示数据失败：{ex.Message}", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
