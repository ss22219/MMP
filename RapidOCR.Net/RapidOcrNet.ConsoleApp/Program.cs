using System.Drawing;
using System.Drawing.Imaging;

namespace RapidOcrNet.ConsoleApp
{
    internal class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                args = new string[]
                {
                    "img_10.jpg",
                    "rotated.PNG",
                    "rotated2.PNG",
                    "1997.png",
                    "5090.FontNameList.1_raw.png",
                    "5090.FontNameList.2_raw.png"
                };
            }

            using var ocrEngin = new RapidOcr();
            ocrEngin.InitModels(
                @"C:\Users\gool\MMP\onnx\ch_PP-OCRv5_mobile_det.onnx",
                @"C:\Users\gool\MMP\onnx\ch_ppocr_mobile_v2.0_cls_infer.onnx",
                @"C:\Users\gool\MMP\onnx\ch_PP-OCRv5_rec_mobile_infer.onnx",
                @"C:\Users\gool\MMP\onnx\ppocrv5_dict.txt",
                12
            );
            while ( true ) 
            foreach (var path in args)
            {
                ProcessImage(ocrEngin, path);
            }

            Console.WriteLine("Bye, RapidOcrNet!");
            Console.ReadKey();
        }

        static void ProcessImage(RapidOcr ocrEngin, string targetImg)
        {
            Console.WriteLine($"Processing {targetImg}");
            using (Bitmap originSrc = new Bitmap(targetImg))
            {
                OcrResult ocrResult = ocrEngin.Detect(originSrc, RapidOcrOptions.Default);
                Console.WriteLine(ocrResult.ToString());
                Console.WriteLine(ocrResult.StrRes);
                Console.WriteLine();

                using (Graphics g = Graphics.FromImage(originSrc))
                using (Pen pen = new Pen(Color.Red, 2))
                {
                    foreach (var block in ocrResult.TextBlocks)
                    {
                        var points = block.BoxPoints;
                        g.DrawLine(pen, points[0], points[1]);
                        g.DrawLine(pen, points[1], points[2]);
                        g.DrawLine(pen, points[2], points[3]);
                        g.DrawLine(pen, points[3], points[0]);
                    }
                }

                originSrc.Save(Path.ChangeExtension(targetImg, "_ocr.png"), ImageFormat.Png);
            }
        }
    }
}
