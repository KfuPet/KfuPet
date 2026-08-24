using System.Windows.Controls;

namespace KfuPet.Core.Rendering
{
    public class RenderContext
    {
        public Canvas Canvas { get; }

        public RenderContext(Canvas canvas)
        {
            Canvas = canvas;
        }
    }
}