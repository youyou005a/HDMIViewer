using System;
using System.Threading;
using System.Windows;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;

namespace HDMIViewer
{
    // 显式继承 System.Windows.Window 彻底解决 CS0104 命名空间冲突
    public partial class MainWindow : System.Windows.Window
    {
        private VideoCapture? _capture;
        private Thread? _thread;
        private bool _running;

        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;
        }

        // ====== 窗口加载时：自动探测真实的硬件通道 ======
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            DeviceComboBox.Items.Clear();
            StatusText.Text = "正在扫描本地视频捕获设备，请稍候...";

            // 在后台启动一个轻量扫描，避免主界面卡顿
            ThreadPool.QueueUserWorkItem(_ =>
            {
                int detectedCount = 0;

                // OpenCV 探测循环：依次尝试打开通道 0, 1, 2, 3
                for (int i = 0; i < 4; i++)
                {
                    try
                    {
                        // 仅做开启测试，不读取画面
                        using var testCap = new VideoCapture(i);
                        if (testCap.IsOpened())
                        {
                            int index = i;
                            detectedCount++;
                            // 切回主线程塞入下拉框
                            Dispatcher.Invoke(() =>
                            {
                                DeviceComboBox.Items.Add($"通道 {index}: 可用的视频设备");
                            });
                        }
                    }
                    catch
                    {
                        // 忽略测试失败的通道
                    }
                }

                // 扫描完成后更新界面状态
                Dispatcher.Invoke(() =>
                {
                    if (detectedCount > 0)
                    {
                        DeviceComboBox.SelectedIndex = 0;
                        StatusText.Text = $"扫描成功！共发现 {detectedCount} 个可用的视频设备通道。";
                    }
                    else
                    {
                        // 如果连一个可开启的都没有，默认塞入 0、1 通道允许用户强行尝试
                        DeviceComboBox.Items.Add("通道 0: 默认通道");
                        DeviceComboBox.Items.Add("通道 1: 备用通道");
                        DeviceComboBox.SelectedIndex = 0;
                        StatusText.Text = "未自动检测到活动的采集卡，已生成默认通道。您可以尝试直接开启。";
                    }
                });
            });
        }

        // ====== 打开采集卡 ======
        private void Open_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_running) return;

                // 1. 解析用户在下拉框里选中的真实通道数字
                int selectedIndex = 0;
                if (DeviceComboBox.SelectedItem != null)
                {
                    string itemText = DeviceComboBox.SelectedItem.ToString() ?? "";
                    // 从 "通道 X: ..." 文本中提取数字
                    if (itemText.Contains("通道 "))
                    {
                        string numStr = itemText.Replace("通道 ", "").Split(':')[0];
                        int.TryParse(numStr, out selectedIndex);
                    }
                }

                // 2. 精准开启选中的通道
                _capture = new VideoCapture(selectedIndex);

                // 2. 精准开启选中的通道
                _capture = new VideoCapture(selectedIndex);

                // ====== 强行加入以下配置（采集卡关键握手信号） ======
                // 强制使用 MJPEG 编码（绝大多数采集卡最兼容的格式）
                _capture.Set(VideoCaptureProperties.FourCC, FourCC.FromString("MJPG"));

                // 强行把分辨率降到 1080P 或 720P 尝试握手（防止4K超限黑屏）
                _capture.Set(VideoCaptureProperties.FrameWidth, 1920);
                _capture.Set(VideoCaptureProperties.FrameHeight, 1080);
                // ==================================================

                if (!_capture.IsOpened())
                {
                    StatusText.Text = $"打开通道 {selectedIndex} 失败。可能设备已被其他软件占用。";
                    _capture.Dispose();
                    return;
                }

                _running = true;

                // 3. 锁定界面元素，防止重复触发
                if (sender is System.Windows.Controls.Button btn)
                {
                    btn.IsEnabled = false;
                }
                DeviceComboBox.IsEnabled = false;

                // 4. 启动画面渲染线程
                _thread = new Thread(ReadFrame);
                _thread.IsBackground = true;
                _thread.Start();

                StatusText.Text = "采集卡连接成功，正在接收视频流...";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"启动异常: {ex.Message}";
            }
        }

        // ====== 读取视频帧（后台线程） ======
        private void ReadFrame()
        {
            while (_running)
            {
                try
                {
                    using var mat = new Mat();
                    _capture?.Read(mat);

                    if (!mat.Empty())
                    {
                        ShowFrame(mat);
                    }

                    // 10ms 休眠保证画面流畅且不卡死 CPU
                    Thread.Sleep(10);
                }
                catch
                {
                    // 捕捉异常，防止硬件意外断开时崩溃
                }
            }
        }

        // ====== 高性能画面渲染 ======
        private void ShowFrame(Mat mat)
        {
            try
            {
                var source = WriteableBitmapConverter.ToWriteableBitmap(mat);
                source.Freeze(); // 冻结以允许跨线程访问

                Dispatcher.Invoke(() =>
                {
                    VideoImage.Source = source;
                });
            }
            catch
            {
                // 忽略渲染期间的微小异常
            }
        }

        // ====== 窗口关闭时彻底释放资源 ======
        protected override void OnClosed(EventArgs e)
        {
            _running = false;

            if (_thread != null && _thread.IsAlive)
            {
                _thread.Join(100);
            }

            try
            {
                _capture?.Release();
                _capture?.Dispose();
            }
            catch { }

            base.OnClosed(e);
        }
    }
}