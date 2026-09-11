using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MockApp
{
    internal class Program
    {// ETX (End of Text) character is ASCII 3
        private const char ETX = (char)3;
        private static readonly Random _random = new Random();
        static void Main(string[] args)
        {
            Console.Title = "噪音计模拟器 (Noise Meter Simulator)";

            // --- 1. 获取用户配置 ---
            Console.WriteLine("--- 噪音计模拟器配置 ---");
            string portName = GetPortName();
            if (string.IsNullOrEmpty(portName)) return; // 用户未选择串口则退出
            int baudRate = GetBaudRate();
            int interval = GetInterval();

            SerialPort serialPort = null;
            try
            {
                // --- 2. 初始化并打开串口 ---
                serialPort = new SerialPort(portName, baudRate)
                {
                    Encoding = Encoding.ASCII // 确保使用 ASCII 编码
                };
                serialPort.Open();
                Console.WriteLine("\n-------------------------------------------------");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"成功打开串口 {portName}，波特率 {baudRate}。");
                Console.WriteLine($"每 {interval}ms 发送一次数据。");
                Console.ResetColor();
                Console.WriteLine("按任意键停止发送...");
                Console.WriteLine("-------------------------------------------------\n");
                // --- 3. 循环发送数据 ---
                while (!Console.KeyAvailable) // 循环直到用户按下键盘
                {
                    // 生成一条随机噪音数据
                    double baseNoise = 55.0; // 基础值
                    double fluctuation = (_random.NextDouble() - 0.5) * 20.0; // 在 +/- 10.0 范围内波动
                    double noiseValue = baseNoise + fluctuation;
                    // 格式化为 "AWAA, 63.1dBA" 形式
                    string valueString = noiseValue.ToString("F1", CultureInfo.InvariantCulture);
                    string dataFrame = $"AWAA, {valueString}dBA";

                    // 附加结束符
                    string messageToSend = dataFrame + ETX;

                    // 发送数据
                    serialPort.Write(messageToSend);

                    // 在控制台显示发送的内容 (为了可读性，将不可见的 ETX 替换为 <ETX>)
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] 发送 -> {dataFrame.Replace(ETX.ToString(), "<ETX>")}");

                    // 等待指定的间隔
                    Thread.Sleep(interval);
                }
            }
            catch (Exception ex)
            {
                // --- 4. 错误处理 ---
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n发生错误: {ex.Message}");
                Console.ResetColor();
            }
            finally
            {
                // --- 5. 清理资源 ---
                if (serialPort != null && serialPort.IsOpen)
                {
                    serialPort.Close();
                    Console.WriteLine("\n-------------------------------------------------");
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("发送已停止，串口已关闭。");
                    Console.ResetColor();
                }
            }
            Console.WriteLine("按任意键退出程序...");
            Console.ReadKey();
        }
        private static string GetPortName()
        {
            Console.WriteLine("\n可用的串口列表:");
            string[] portNames = SerialPort.GetPortNames();
            if (portNames.Length == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("系统中未找到任何串口。请确保已安装虚拟串口驱动。");
                Console.ResetColor();
                return null;
            }
            foreach (var port in portNames)
            {
                Console.WriteLine($" - {port}");
            }

            string chosenPort;
            while (true)
            {
                Console.Write("\n请输入要使用的串口号 (例如 COM10): ");
                chosenPort = Console.ReadLine().ToUpper();
                if (portNames.Contains(chosenPort))
                {
                    return chosenPort;
                }
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"错误：'{chosenPort}' 不是一个有效的串口。请从以上列表中选择。");
                Console.ResetColor();
            }
        }

        private static int GetBaudRate()
        {
            int baudRate;
            while (true)
            {
                Console.Write("请输入波特率 (默认 9600): ");
                string input = Console.ReadLine();
                if (string.IsNullOrEmpty(input))
                {
                    return 9600; // 用户直接回车，使用默认值
                }
                if (int.TryParse(input, out baudRate) && baudRate > 0)
                {
                    return baudRate;
                }
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("错误：请输入一个有效的正整数作为波特率。");
                Console.ResetColor();
            }
        }
        private static int GetInterval()
        {
            int interval;
            while (true)
            {
                Console.Write("请输入发送间隔(ms) (默认 200): ");
                string input = Console.ReadLine();
                if (string.IsNullOrEmpty(input))
                {
                    return 200; // 使用默认值
                }
                if (int.TryParse(input, out interval) && interval > 0)
                {
                    return interval;
                }
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("错误：请输入一个有效的正整数作为间隔时间。");
                Console.ResetColor();
            }
        }
    }
}