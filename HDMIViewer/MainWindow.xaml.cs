using System.Windows;
using DirectShowLib;

namespace HDMIViewer
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Scan_Click(object sender, RoutedEventArgs e)
        {
            DeviceList.Items.Clear();

            try
            {
                var devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);

                foreach (var d in devices)
                {
                    DeviceList.Items.Add(d.Name);
                }
            }
            catch (System.Exception ex)
            {
                DeviceList.Items.Add("错误：" + ex.Message);
            }
        }
    }
}