﻿using System;

namespace KIP7.ImageProcessors {
	public class ImageProcessorSelector {
		public string Title { get; set; }
		public Type ImageProcessor { get; set; }
	}
}
