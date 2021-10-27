using HtmlAgilityPack;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;

namespace ECSMD
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        string main_thread_url_text = "";

        static public string GetSteamTitle(string url)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
            WebClient wc = new WebClient();
            Stream st = wc.OpenRead(url);
            StreamReader sr = new StreamReader(st, Encoding.UTF8);

            HtmlDocument html = new HtmlDocument();
            html.Load(sr);

            // HTMLから(複数の)titleタグを探す
            var titles = html.DocumentNode.Descendants("title");

            // 最初のtitleタグのノードを取り出す
            var titleNode = titles.First();

            // titleタグの値(サイトのタイトルといて表示される部分)を取得する
            return titleNode.InnerText;
        }


        public MainWindow()
        {
            InitializeComponent();
            log_init();
        }

        private void licence_show_Click(object sender, RoutedEventArgs e)
        {
            new licence().Show();
        }

        private int MOD_URL_check()
        {
            if (main_thread_url_text == "")
            {
                outlog("URLチェックエラー:URLが入力されていません");
                return 1;
            }
            else
            {
                if (main_thread_url_text.Contains("https://steamcommunity.com/sharedfiles/filedetails/?id="))
                {
                    string Title = "";
                    
                    Thread thread = new Thread(new ThreadStart(() =>
                    {
                        Title = GetSteamTitle(main_thread_url_text);
                    }));

                    thread.Start();
                    thread.Join();


                    //System.Diagnostics.Debug.WriteLine(Title);

                    bool success = true;

                    if (Title.Contains("Error"))
                    {
                        success = false;
                    }

                    if (success)
                    {
                        outlog("URLチェック:正常なURLです");
                        return 0;
                    }
                    else
                    {
                        outlog("URLチェックエラー:Steamからエラーが返されました");
                        return 1;
                    }
                }
                else
                {
                    outlog("URLチェックエラー:SteamWorkshopのURLではありません");
                    return 1;
                }
            }
        }

        private void settings_ok_Click(object sender, RoutedEventArgs e)
        {
            main_thread_url_text = MOD_URL.Text;

            Thread thread = new Thread(new ThreadStart(() =>
            {
                doit();
            }));

            thread.Start();

        }

        private void doit()
        {
            //URLチェック
            outlog("URLチェック中...");
            if (MOD_URL_check() != 0)
            {
                return;
            }

            //SWDのチェック
            outlog("ダウンローダーのチェック中");

            string dir_path = Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location);

            string swd_file_name = dir_path + @"\swd\";

            if (Environment.Is64BitOperatingSystem)
            {
                swd_file_name += "swd-x64.exe";
            }
            else
            {
                swd_file_name += "swd-x86.exe";
            }

            if (!File.Exists(swd_file_name))
            {
                outlog("エラー:MODダウンローダーが見つかりません！");
                return;
            }

            outlog("このMODダウンローダーを使用します " + swd_file_name);

            if (!Directory.Exists(dir_path + @"\tmp\"))
            {
                Directory.CreateDirectory(dir_path + @"\tmp\");
            }

            //一時ファイル削除
            del_tmp_file();

            //SWDでMODをダウンロードする
            Process p = new Process();
            p.StartInfo.FileName = swd_file_name;
            p.StartInfo.WorkingDirectory = dir_path + @"\tmp\";
            p.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardInput = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.Arguments = main_thread_url_text;

            outlog("ダウンロード開始...");

            p.Start();
            Stopwatch sw = new Stopwatch();
            sw.Start();
            bool exit_time = false;

            while (!p.HasExited||exit_time)
            {
                outlog("[SWD]" + p.StandardOutput.ReadLine());
                if(sw.ElapsedMilliseconds == 30000)
                {
                    exit_time = true;
                }
            }

            outlog(p.StandardOutput.ReadToEnd());
            if (exit_time)
            {
                outlog("エラー:ダウンローダーがタイムアウトしました");
                del_tmp_file();
                return;
            }

            //zipを解凍して配置
            //%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods

            var modPath = Environment.GetEnvironmentVariable("LOCALAPPDATA")+ @"\Colossal Order\Cities_Skylines\Addons\Mods";

            if (!Directory.Exists(modPath))
            {
                outlog("エラー:対象MODフォルダが見つかりません: "+modPath);
                del_tmp_file();
                return;
            }


            //解凍
            string[] files = Directory.GetFiles(dir_path + @"\tmp\", "*.zip");
            foreach (string f in files)
            {
                ZipFile.ExtractToDirectory(f, dir_path + @"\tmp\");
            }

            string[] dll_files = Directory.GetFiles(dir_path + @"\tmp\", "*.dll", SearchOption.AllDirectories);

            string dir_name = main_thread_url_text.Replace("https://steamcommunity.com/sharedfiles/filedetails/?id=", "");

            string mod_dir = modPath + @"\" + dir_name;

            Directory.CreateDirectory(mod_dir);

            int num_error = 0;

            //modフォルダへコピー
            foreach (string f in dll_files)
            {
                string f_name = Path.GetFileName(f);
                try
                {
                    File.Copy(f, mod_dir + @"\" + f_name);
                    outlog("ファイルをコピーしました:" + mod_dir + @"\" + f_name);

                }catch(Exception e)
                {
                    outlog("ファイルコピーエラー:例外をスローしました: "+e.Message);
                    num_error++;
                }

            }

            //終了
            del_tmp_file();
            if(num_error != 0)
            {
                outlog("すべての操作を完了しましたがファイルコピーでエラーが発生した可能性があります。詳しくはログを参照してください。エラー数:" + num_error);
            }
            else
            {
                outlog("配置フォルダ: "+ mod_dir);
                outlog("✔すべての操作が正常に終了しました！");
            }


        }

        private void del_tmp_file()
        {
            string folderFrom = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)+ @"\tmp\";

            if (Directory.Exists(folderFrom))
            {
                foreach (string pathFrom in Directory.EnumerateFiles(folderFrom, "*"))
                {
                    outlog("一時ファイル削除: "+pathFrom);
                    File.Delete(pathFrom);
                }
            }

        }

        public void outlog(string val,bool ignore_n=false)
        {
            DateTime dt = DateTime.Now;

            string n;
            if (ignore_n)
            {
                n = "";
            }
            else
            {
                n = "\n";
            }


            this.Dispatcher.Invoke((Action)(() =>
            {            
                OUTPUT_LOG.AppendText("["+ dt.ToString("HH:mm:ss") +"] "+ val + n);
                OUTPUT_LOG.Focus();
                OUTPUT_LOG.CaretIndex = OUTPUT_LOG.Text.Length;
                OUTPUT_LOG.ScrollToEnd();
            }));
        }

        private void log_init()
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            System.Version ver = asm.GetName().Version;
            var dt = File.GetLastWriteTimeUtc(Assembly.GetExecutingAssembly().Location);
            dt += new TimeSpan(9, 0, 0);

            outlog("Epic Cities Skylines Mod Downloader(ECSMD) ver: " + ver);
            outlog("Build Date: " + dt);
            outlog("Ready.");
            outlog("------");
        }
    }
}
