using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;

namespace HDMIViewer
{
    public partial class MainWindow : System.Windows.Window
    {
        private VideoCapture? _capture;
        private Thread? _thread;
        private bool _running;

        // 记录当前是否处于全屏状态
        private bool _isFullScreen = false;

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

                OpenButton.IsEnabled = false;
                StopButton.IsEnabled = true;
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

        private void StopCapture()
        {
            if (!_running) return;

            _running = false;

            if (_thread != null && _thread.IsAlive)
            {
                _thread.Join(150);
            }

            try
            {
                _capture?.Release();
                _capture?.Dispose();
                _capture = null;
            }
            catch { }

            Dispatcher.Invoke(() =>
            {
                VideoImage.Source = null;

                OpenButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                ChannelComboBox.IsEnabled = true;
                WidthTextBox.IsEnabled = true;
                HeightTextBox.IsEnabled = true;
                FormatComboBox.IsEnabled = true;
            });
        }

        // ====== 核心全屏切换逻辑 ======
        private void ToggleFullScreen()
        {
            if (!_isFullScreen)
            {
                // 变成全屏状态
                this.WindowStyle = WindowStyle.None; // 砍掉系统自带的标题栏和边框
                this.WindowState = WindowState.Maximized; // 窗口最大化（由于Style为None，它会自动盖住底部的任务栏）

                // 隐藏我们的控制区域和状态栏，腾出整张屏幕给视频
                ControlRow.Height = new GridLength(0);
                StatusRow.Height = new GridLength(0);

                _isFullScreen = true;
            }
            else
            {
                // 退出全屏恢复常规窗口
                this.WindowStyle = WindowStyle.SingleBorderWindow; // 恢复系统标题栏
                this.WindowState = WindowState.Normal; // 恢复常规大小

                // 重新把控制区域和状态栏显示出来
                ControlRow.Height = GridLength.Auto;
                StatusRow.Height = GridLength.Auto;

                _isFullScreen = false;
            }
        }

        // 按钮点击触发全屏
        private void FullScreen_Click(object sender, RoutedEventArgs e)
        {
            ToggleFullScreen();
        }

        // 双击视频画面触发全屏
        private void VideoBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 检查是否是双击
            if (e.ClickCount == 2)
            {
                ToggleFullScreen();
            }
        }

        // 全局键盘监听
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // 如果处于全屏状态下按下了 Esc 键，直接退出全屏
            if (e.Key == Key.Escape && _isFullScreen)
            {
                ToggleFullScreen();
            }
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
                    if (_running)
                    {
                        VideoImage.Source = source;
                    }
                });
            }
            catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            StopCapture();
            base.OnClosed(e);
        }
    }
}