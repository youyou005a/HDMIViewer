using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;

namespace HDMIViewer
{
    public partial class MainWindow : System.Windows.Window
    {
        private VideoCapture? _capture;
        private Thread? _thread;
        private bool _running;

        public MainWindow()
        {
            InitializeComponent();
        }

        // ====== 打开采集卡 ======
        private void Open_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_running) return;

                int channel = ChannelComboBox.SelectedIndex >= 0 ? ChannelComboBox.SelectedIndex : 0;

                if (!int.TryParse(WidthTextBox.Text, out int width)) width = 1920;
                if (!int.TryParse(HeightTextBox.Text, out int height)) height = 1080;

                _capture = new VideoCapture(channel);

                string selectedFormat = (FormatComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "MJPEG";
                if (selectedFormat == "MJPEG")
                {
                    _capture.Set(VideoCaptureProperties.FourCC, FourCC.FromString("MJPG"));
                }
                else if (selectedFormat == "YUY2")
                {
                    _capture.Set(VideoCaptureProperties.FourCC, FourCC.FromString("YUY2"));
                }

                _capture.Set(VideoCaptureProperties.FrameWidth, width);
                _capture.Set(VideoCaptureProperties.FrameHeight, height);

                if (!_capture.IsOpened())
                {
                    StatusText.Text = $"打开通道 {channel} 失败。请检查连接或更换参数。";
                    _capture.Dispose();
                    return;
                }

                int actualWidth = (int)_capture.Get(VideoCaptureProperties.FrameWidth);
                int actualHeight = (int)_capture.Get(VideoCaptureProperties.FrameHeight);

                _running = true;

                // 切换UI控件状态
                OpenButton.IsEnabled = false;
                StopButton.IsEnabled = true;      // 启用停止按钮
                ChannelComboBox.IsEnabled = false;
                WidthTextBox.IsEnabled = false;
                HeightTextBox.IsEnabled = false;
                FormatComboBox.IsEnabled = false;

                _thread = new Thread(ReadFrame);
                _thread.IsBackground = true;
                _thread.Start();

                StatusText.Text = $"连接成功！当前分辨率: {actualWidth} x {actualHeight} | 格式: {selectedFormat}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"启动异常: {ex.Message}";
            }
        }

        // ====== 停止采集 ======
        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            StopCapture();
            StatusText.Text = "已停止采集";
        }

        // ====== 核心停止逻辑（复用抽取） ======
        private void StopCapture()
        {
            if (!_running) return;

            _running = false;

            // 等待后台抓图线程安全退出
            if (_thread != null && _thread.IsAlive)
            {
                _thread.Join(150);
            }

            try
            {
                // 彻底释放采集卡占用
                _capture?.Release();
                _capture?.Dispose();
                _capture = null;
            }
            catch { }

            // 切回主线程重置 UI 状态
            Dispatcher.Invoke(() =>
            {
                VideoImage.Source = null; // 清空最后一帧画面，恢复黑色背景

                // 恢复所有控件的可点击状态
                OpenButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                ChannelComboBox.IsEnabled = true;
                WidthTextBox.IsEnabled = true;
                HeightTextBox.IsEnabled = true;
                FormatComboBox.IsEnabled = true;
            });
        }

        // ====== 后台抓图线程 ======
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
                    Thread.Sleep(10);
                }
                catch
                {
                    // 防崩
                }
            }
        }

        // ====== 画面渲染 ======
        private void ShowFrame(Mat mat)
        {
            try
            {
                var source = WriteableBitmapConverter.ToWriteableBitmap(mat);
                source.Freeze();

                Dispatcher.Invoke(() =>
                {
                    if (_running) // 确保在停止的瞬间不再往里塞图
                    {
                        VideoImage.Source = source;
                    }
                });
            }
            catch { }
        }

        // ====== 窗口关闭时彻底释放资源 ======
        protected override void OnClosed(EventArgs e)
        {
            StopCapture();
            base.OnClosed(e);
        }
    }
}