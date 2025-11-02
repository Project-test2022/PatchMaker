using PatchMaker.Config;
using PatchMaker.Services;
using System;
using System.Text;

namespace PatchMaker
{
    internal class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            var config = ConfigLoader.LoadOrPrompt("appsettings.json");
            var service = new PatchMakerService();
            var isSuccess = service.Run(config);

            Console.WriteLine();
            Console.WriteLine("処理が完了しました。閉じるには何かキーを押してください...");
            Console.ReadKey(true);

            return isSuccess ? 0 : 1;
        }
    }
}
