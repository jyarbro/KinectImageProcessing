﻿using System;
using System.Collections.Generic;

namespace KIP2.Models.ImageProcessors {
	/// <summary>
	/// The base for all image processors
	/// </summary>
	public abstract class ImageProcessorBase {
		public int FocusRegionArea;
		public int FocusRegionWidth;

		public int FocusPartArea;
		public int FocusPartWidth;
		public int FocusPartHorizontalCount;
		public int FocusPartTotalCount;

		public List<int[]> FocusPartOffsets;
		public List<byte[]> FocusParts;

		public int SampleGap;
		public int SampleByteCount;
		public int SampleCenterOffset;

		public int[] SampleOffsets;

		public int[] CompressedSensorData;
		public byte[] ColorSensorData;
		public short[] ImageDepthData;

		public Point ImageMax = new Point { X = 640, Y = 480 };
		public Point ImageMid = new Point { X = 320, Y = 240 };
		public Point FocalPoint = new Point();

		public Rectangle Window;

		public int ByteCount;
		public int PixelCount;

		public byte[] OutputArray;

		public int FocalPointOffset { get; set; }

		public ImageProcessorBase() {
			Window = new Rectangle(-ImageMid.X, -ImageMid.Y, ImageMid.X, ImageMid.Y);

			PixelCount = ImageMid.X * ImageMid.Y;
			ByteCount = ImageMax.X * ImageMax.Y * 4;

			CompressedSensorData = new int[PixelCount];
			OutputArray = new byte[ByteCount];

			SampleGap = 10;

			FocusPartWidth = 11;
			FocusPartArea = FocusPartWidth * FocusPartWidth; // 121

			FocusRegionWidth = 99;
			FocusRegionArea = FocusRegionWidth * FocusRegionWidth; // 9801

			if (FocusRegionWidth % FocusPartWidth > 0)
				throw new Exception("Focus area width must be divisible by sample area width");

			FocusPartHorizontalCount = FocusRegionWidth / FocusPartWidth; // 9
			FocusPartTotalCount = FocusPartHorizontalCount * FocusPartHorizontalCount; // 81

			FocusPartOffsets = new List<int[]>();
			FocusParts = new List<byte[]>();

			SampleOffsets = PrepareSquareOffsets(FocusPartArea, ImageMax.X, false);
		}

		/// <summary>
		/// Optional method for overriding.
		/// </summary>
		public virtual void Prepare() { }

		/// <summary>
		/// Requires override
		/// </summary>
		public abstract byte[] ProcessImage();

		/// <summary>
		/// A universal method for calculating all of the linear offsets for a given square area
		/// </summary>
		public int[] PrepareSquareOffsets(int size, int stride, bool byteMultiplier = true) {
			if (size % 2 == 0)
				throw new Exception("Odd sizes only!");

			var offsets = new int[size];
			var areaBox = GetCenteredBox(size);

			var offset = 0;

			for (int yOffset = areaBox.Origin.Y; yOffset <= areaBox.Extent.Y; yOffset++) {
				for (int xOffset = areaBox.Origin.X; xOffset <= areaBox.Extent.X; xOffset++) {
					offsets[offset] = xOffset + (yOffset * stride);

					if (byteMultiplier)
						offsets[offset] = offsets[offset] * 4;

					offset++;
				}
			}

			return offsets;
		}

		public Rectangle GetCenteredBox(int size) {
			var areaMax = Convert.ToInt32(Math.Floor(Math.Sqrt(size) / 2));
			var areaMin = areaMax * -1;

			return new Rectangle(areaMin, areaMin, areaMax, areaMax);
		}

		/// <summary>
		/// Calculates offsets used in sampling.
		/// </summary>
		public void PrepareSampleOffsets() {
			SampleByteCount = FocusPartArea * 4;
			SampleOffsets = PrepareSquareOffsets(FocusPartArea, ImageMax.X);
			SampleCenterOffset = Convert.ToInt32(Math.Floor(Math.Sqrt(FocusPartArea) / 2));
		}

		/// <summary>
		/// Calculatets offsets used when focal region is split into parts
		/// </summary>
		public void PrepareFocusPartOffsets() {
			var focusOffsets = PrepareSquareOffsets(FocusRegionArea, ImageMax.X);

			for (int i = 0; i < FocusPartTotalCount; i++) {
				FocusParts.Add(new byte[FocusPartArea * 4]);
				FocusPartOffsets.Add(new int[FocusPartArea]);
			}

			for (var i = 0; i < focusOffsets.Length; i++) {
				var y = i / FocusRegionWidth;
				var x = i % FocusRegionWidth;

				var focusPartRow = y / FocusPartWidth;
				var focusPartCol = x / FocusPartWidth;

				var focusPartOffset = focusPartCol + (focusPartRow * FocusPartHorizontalCount);

				FocusPartOffsets[focusPartOffset][i % FocusPartArea] = focusOffsets[i];
			}
		}

		/// <summary>
		/// Compresses ColorSensorData by 50%
		/// </summary>
		public void PrepareCompressedSensorData() {
			int y;
			int x;
			int pixel;
			int compressedValue;

			for (y = 0; y < ImageMax.Y; y += 2) {
				for (x = 0; x < ImageMax.X; x += 2) {
					pixel = ((y * ImageMax.X) + x) * 4;

					compressedValue =
						ColorSensorData[pixel] + ColorSensorData[pixel + 1] + ColorSensorData[pixel + 2]
						+ ColorSensorData[pixel + 4] + ColorSensorData[pixel + 5] + ColorSensorData[pixel + 6];

					pixel = (((y + 1) * ImageMax.X) + x) * 4;

					compressedValue +=
						ColorSensorData[pixel] + ColorSensorData[pixel + 1] + ColorSensorData[pixel + 2]
						+ ColorSensorData[pixel + 4] + ColorSensorData[pixel + 5] + ColorSensorData[pixel + 6];

					CompressedSensorData[((y / 2) * ImageMid.X) + (x / 2)] = compressedValue / 12;
				}
			}
		}

		/// <summary>
		/// Calculates the closest focal point near a target coordinate
		/// </summary>
		public Point GetNearestFocalPoint(Rectangle window, Point target) {
			Func<int, int> measurement = (pixel) => {
				return ImageDepthData[pixel];
			};

			Func<int, int, bool> valueComparison = (newValue, currentValue) => {
				return newValue <= currentValue;
			};

			return GetMeasuredFocalPoint(window, target, measurement, valueComparison);
		}

		/// <summary>
		/// Calculates the brightest focal point near a target coordinate
		/// </summary>
		public Point GetBrightestFocalPoint(Rectangle window, Point target) {
			Func<int, int> measurement = (pixel) => {
				pixel = pixel * 4;
				var measuredValue = 0;

				foreach (var sampleOffset in SampleOffsets) {
					if (pixel + sampleOffset > 0 && pixel + sampleOffset < ByteCount) {
						measuredValue += ColorSensorData[pixel + sampleOffset] + ColorSensorData[pixel + sampleOffset + 1] + ColorSensorData[pixel + sampleOffset + 2];
					}
				}

				return measuredValue;
			};

			Func<int, int, bool> valueComparison = (newValue, currentValue) => {
				return newValue >= currentValue;
			};

			return GetMeasuredFocalPoint(window, target, measurement, valueComparison);
		}

		public Point GetMeasuredFocalPoint(Rectangle window, Point target, Func<int, int> measurement, Func<int, int, bool> valueComparison) {
			var focalPoint = new Point();

			int x;
			int y;
			int yOffset;
			int measuredValue;

			var xSq = Math.Pow(Math.Abs(ImageMid.X), 2);
			var ySq = Math.Pow(Math.Abs(ImageMid.Y), 2);

			var closestPixelDistance = Math.Sqrt(xSq + ySq);

			double distanceFromCenter;
			int highestMeasuredValue = 0;

			for (y = target.Y + window.Origin.Y; y < target.Y + window.Extent.Y; y += SampleGap) {
				yOffset = y * ImageMax.X;

				for (x = target.X + window.Origin.X; x < target.X + window.Extent.X; x += SampleGap) {
					measuredValue = measurement(yOffset + x);

					if (valueComparison(measuredValue, highestMeasuredValue)) {
						xSq = Math.Pow(Math.Abs(x - target.X), 2);
						ySq = Math.Pow(Math.Abs(y - target.Y), 2);

						distanceFromCenter = Math.Sqrt(xSq + ySq);

						// speed cheat - not true hypoteneuse!
						//var distanceFromCenter = Math.Abs(x - Target.X) + Math.Abs(y - Target.Y);

						if (distanceFromCenter <= closestPixelDistance) {
							closestPixelDistance = distanceFromCenter;
							highestMeasuredValue = measuredValue;

							focalPoint.X = x;
							focalPoint.Y = y;
						}
					}
				}
			}

			return focalPoint;
		}

		/// <summary>
		/// Add blue pixels for sampling grid
		/// </summary>
		public void OverlaySampleGrid() {
			for (int y = 0; y < ImageMax.Y; y += SampleGap) {
				var yOffset = y * ImageMax.X;

				for (int x = 0; x < ImageMax.X; x += SampleGap) {
					var pixel = (yOffset + x) * 4;

					OutputArray[pixel + 0] = 255;
					OutputArray[pixel + 1] = 0;
					OutputArray[pixel + 2] = 0;
				}
			}
		}

		/// <summary>
		/// Add color spot to highlight focal point
		/// </summary>
		public void OverlayFocalPoint(int color) {
			foreach (var sampleOffset in SampleOffsets) {
				var sampleByteOffset = sampleOffset * 4;

				if (FocalPointOffset + sampleByteOffset > 0 && FocalPointOffset + sampleByteOffset < ByteCount) {
					OutputArray[FocalPointOffset + sampleByteOffset] = (byte) (color == 1 ? 255 : 0);
					OutputArray[FocalPointOffset + sampleByteOffset + 1] = (byte)(color == 2 ? 255 : 0);
					OutputArray[FocalPointOffset + sampleByteOffset + 2] = (byte)(color == 3 ? 255 : 0);
				}
			}
		}

		/// <summary>
		/// Simply copies the input to the output. Useful in most situations.
		/// </summary>
		public virtual void PrepareOutput() {
			Buffer.BlockCopy(ColorSensorData, 0, OutputArray, 0, ColorSensorData.Length);
		}
	}
}
