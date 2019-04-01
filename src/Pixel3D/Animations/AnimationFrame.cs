// Copyright � Conatus Creative, Inc. All rights reserved.
// Licensed under the Apache 2.0 License. See LICENSE.md in the project root for license terms.
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Pixel3D.Extensions;
using Pixel3D.FrameworkExtensions;
using Pixel3D.Serialization;

namespace Pixel3D.Animations
{
    public class AnimationFrame
    {
        public AnimationFrame()
        {
            masks = new OrderedDictionary<string, Mask>();

            outgoingAttachments = new OrderedDictionary<string, OutgoingAttachment>();
			incomingAttachments = new TagLookup<Position>();
            triggers = null;
        }
		
        public Cel firstLayer;

        public LayersList layers { get { return new LayersList(this); } }

        public Position shadowOffset;

        // Animation:
        /// <summary>Number of ticks (frames at 60FPS) that this animation frame lasts for.</summary>
        public int delay;

        // Gameplay:
        /// <summary>Gameplay position offset to apply at the start of this frame.</summary>
        public Position positionDelta;
        public bool SnapToGround { get; set; }

        /// <summary>The layer number where attachments are inserted (before the given layer)</summary>
        public int attachAtLayer;
        /// <summary>True if the layers following attachAtLayer can be drawn over a held sorted object (a "thick"/3D object)</summary>
        /// <remarks>
        /// This is mostly a a backwards-compatibility thing, given that our old animations are not set-up to support this properly.
        /// But it is conceivable that it will be a useful feature for animations that can pick up thick and thin objects.
        /// Basically says: everything above "attachAtLayer" is small -- like a hand or similar.
        /// </remarks>
        public bool canDrawLayersAboveSortedAttachees;

	    public OrderedDictionary<string, Mask> masks;

		public OrderedDictionary<string, OutgoingAttachment> outgoingAttachments;
        public TagLookup<Position> incomingAttachments;

		/// <summary>List of symbols, or null for no triggers this frame.</summary>
        public List<string> triggers;

        public string cue;

        public void AddTrigger(string symbol)
        {
            if(triggers == null)
                triggers = new List<string>();
            triggers.Add(symbol);
        }

        public bool RemoveTrigger(string symbol)
        {
            if (triggers == null)
                return false;
            return triggers.Remove(symbol);
        }
		
        public AnimationFrame(Texture2D texture, Rectangle sourceRectangle, Point origin, int delay) : this()
        {
            layers.Add(new Cel(new Sprite(texture, sourceRectangle, origin)));
            this.delay = delay;
        }

        public AnimationFrame(Cel cel, int delay) : this()
        {
            layers.Add(cel);
            this.delay = delay;
        }

        public AnimationFrame(int delay) : this()
        {
            this.delay = delay;
        }

		#region Alpha Mask and Bounds Handling

        /// <summary>Calculate the maximum world space bounds of all layers in the frame. EDITOR ONLY!</summary>
        public Rectangle CalculateGraphicsBounds()
        {
            Rectangle maxBounds = Rectangle.Empty;
            foreach(var celView in layers)
            {
                maxBounds = RectangleExtensions.UnionIgnoreEmpty(maxBounds, celView.CalculateGraphicsBounds());
            }
            return maxBounds;
        }


        public Mask GetAlphaMaskView()
        {
            Debug.Assert(!Asserts.enabled || masks.HasBaseFallback()); // <- the alpha mask should have been generated by this point

	        Mask mask = masks.GetBaseFallback();
            Debug.Assert(!Asserts.enabled || mask.isGeneratedAlphaMask == true);

            return mask;
        }
		
        /// <param name="celAlphaMasks">Fill with masks created from Cels</param>
        /// <param name="allMasks">Fill with all created masks</param>
        public void RegenerateAlphaMask()
        {
	        masks.TryRemoveBaseFallBack();
            
            // If this frame has a single sprite-containing Cel, generate it directly
            if(firstLayer != null && firstLayer.next == null)
            {
                Mask mask = new Mask
                {
                    data = firstLayer.spriteRef.ResolveRequire().GetAlphaMask(),
                    isGeneratedAlphaMask = true,
                };
                    
                masks.AddBaseFallback(mask);
            }
            else // ... Otherwise, try to create a mask merged from the frame's layers
            {
                List<MaskData> layerMasks = new List<MaskData>();
                foreach(var cel in layers)
                {
                    MaskData maskData = cel.spriteRef.ResolveRequire().GetAlphaMask();
                    if(maskData.Width != 0 && maskData.Height != 0)
                        layerMasks.Add(maskData);
                }

                Rectangle maxBounds = Rectangle.Empty;
                foreach(var maskData in layerMasks)
                {
                    maxBounds = RectangleExtensions.UnionIgnoreEmpty(maxBounds, maskData.Bounds);
                }

                Mask mask = new Mask() { isGeneratedAlphaMask = true };
                mask.data = new MaskData(maxBounds);
                foreach(var layerMask in layerMasks)
                {
                    Debug.Assert(!Asserts.enabled || mask.data.Bounds.Contains(layerMask.Bounds));
                    mask.data.SetBitwiseOrFrom(layerMask);
                }

				masks.AddBaseFallback(mask);
			}
        }

		#endregion

		#region Rendering

        public void Draw(DrawContext drawContext, Position position, bool flipX)
        {
            Draw(drawContext, position, flipX, Color.White);
        }

        public void Draw(DrawContext drawContext, Position position, bool flipX, Color color)
        {
            foreach(var layer in layers)
                layer.Draw(drawContext, position, flipX, color);
        }

        public void DrawBeforeAttachment(DrawContext drawContext, Position position, bool flipX, Color color)
        {
            Debug.Assert(attachAtLayer >= 0);
            // NOTE: Iterating this way because layers is a linked list...
            int i = 0;
            foreach(var layer in layers)
            {
                if(i < attachAtLayer)
                    layer.Draw(drawContext, position, flipX, color);
                else
                    break;
                i++;
            }
        }

        public void DrawAfterAttachment(DrawContext drawContext, Position position, bool flipX, Color color)
        {
            Debug.Assert(attachAtLayer >= 0);
            // NOTE: Iterating this way because layers is a linked list...
            int i = 0;
            foreach(var layer in layers)
            {
                if(i >= attachAtLayer)
                    layer.Draw(drawContext, position, flipX, color);
                i++;
            }
        }
		
        public void GetShadowReceiverHeightmapViews(Position position, bool flipX, List<HeightmapView> output)
        {
            foreach(var layer in layers)
            {
                if(layer.shadowReceiver != null)
                {
                    output.Add(new HeightmapView(layer.shadowReceiver.heightmap, position, flipX));
                }
            }
        }

        #endregion

		#region Soft Rendering

        public Data2D<Color> SoftRender()
        {
            Data2D<Color> output = new Data2D<Color>();
            foreach(var cel in layers)
            {
                if(cel.shadowReceiver != null)
                    continue; // Skip shadow receivers

                var spriteData = cel.spriteRef.ResolveRequire().GetData();

                // Blend with the output
                if(output.Data == null)
                    output = spriteData;
                else
                {
                    output = output.LazyCopyExpandToContain(spriteData.Bounds);

                    for(int y = spriteData.StartY; y < spriteData.EndY; y++) for(int x = spriteData.StartX; x < spriteData.EndX; x++)
                    {
                        Color pixel = spriteData[x, y];
                        if(pixel != Color.Transparent)
                        {
                            if(pixel.A == 0xFF)
                                output[x, y] = pixel;
                            else
                            {
                                Vector4 pixelFloat = pixel.ToVector4();
                                output[x, y] = new Color(pixelFloat + output[x, y].ToVector4() * (1f - pixelFloat.W)); // floating-point blend, because I'm lazy...
                            }
                        }
                    }
                }
            }

            Debug.Assert(!Asserts.enabled || output.Bounds == GetSoftRenderBounds());

            return output;
        }

        /// <summary>EDITOR ONLY!</summary>
        public Rectangle GetSoftRenderBounds()
        {
            Rectangle output = Rectangle.Empty;
            foreach(var cel in layers)
            {
                if(cel.shadowReceiver != null)
                    continue; // Skip shadow receivers

                Sprite sprite = cel.spriteRef.ResolveRequire();
                Rectangle bounds = new Rectangle(-sprite.origin.X, -sprite.origin.Y, sprite.sourceRectangle.Width, sprite.sourceRectangle.Height);

                output = RectangleExtensions.UnionIgnoreEmpty(output, bounds);
            }
            return output;
        }

        #endregion
		
		#region Attachments

        public void AddOutgoingAttachment(string rule, OutgoingAttachment outgoingAttachment)
        {
            if (rule != null)
                outgoingAttachments.Add(rule, outgoingAttachment);
        }

        public void AddIncomingAttachment(TagSet rule, Position position)
        {
            if (rule != null)
                incomingAttachments.Add(rule, position);
        }

        public bool RemoveOutgoingAttachment(OutgoingAttachment outgoingAttachment)
        {
	        string key = null;
	        foreach (var entry in outgoingAttachments)
	        {
		        if (ReferenceEquals(entry.Value, outgoingAttachment))
		        {
			        key = entry.Key;
		        }
	        }
	        if (key != null)
	        {
		        return outgoingAttachments.Remove(key);
	        }
	        return false;
        }

        public bool RemoveMask(Mask mask)
        {
	        string key = null;
	        foreach (var entry in masks)
	        {
		        if (ReferenceEquals(entry.Value, mask))
		        {
			        key = entry.Key;
		        }
			}
			if(key != null)
			{
				return masks.Remove(key);
			}
            return false;
        }

        #endregion

        #region Serialization

        [SerializationIgnoreDelegates]
        public void Serialize(AnimationSerializeContext context)
        {
            context.bw.Write(delay);
            context.bw.Write(positionDelta);
            context.bw.Write(shadowOffset);

            context.bw.Write(SnapToGround);

            // NOTE: This walks the layer linked list twice, but is only O(n), so no biggie
            int layerCount = layers.Count;
            context.bw.Write(layerCount);
            foreach (var cel in layers)
                cel.Serialize(context);

            masks.SerializeOrderedDictionary(context, m => m.Serialize(context));

            outgoingAttachments.SerializeOrderedDictionary(context, oa => oa.Serialize(context));
            incomingAttachments.SerializeTagLookup(context, p => context.bw.Write(p));

            if (triggers == null)
            {
                context.bw.Write(0);
            }
            else
            {
                context.bw.Write(triggers.Count);
                for (var i = 0; i < triggers.Count; i++)
                    context.bw.Write(triggers[i]);
            }

            context.bw.Write(attachAtLayer.Clamp(0, layers.Count));
            context.bw.Write(canDrawLayersAboveSortedAttachees);

            context.bw.WriteNullableString(cue);
        }

        /// <summary>Deserialize into new object instance</summary>
        [SerializationIgnoreDelegates]
        public AnimationFrame(AnimationDeserializeContext context)
        {
            delay = context.br.ReadInt32();
            positionDelta = context.br.ReadPosition();
            shadowOffset = context.br.ReadPosition();

            SnapToGround = context.br.ReadBoolean();

            int layersCount = context.br.ReadInt32();
            if (layersCount > 0)
            {
                Cel currentCel;
                firstLayer = currentCel = new Cel(context);
                for (var i = 1; i < layersCount; i++)
                {
                    currentCel.next = new Cel(context);
                    currentCel = currentCel.next;
                }
            }

            if (context.Version >= 39)
            {
                masks = context.DeserializeOrderedDictionary(() => new Mask(context));
                outgoingAttachments = context.DeserializeOrderedDictionary(() => new OutgoingAttachment(context));
            }
            else
            {
                //
                // Masks:
                {
                    var legacy = context.DeserializeTagLookup(() => new Mask(context));
                    masks = new OrderedDictionary<string, Mask>();
                    foreach (var mask in legacy)
                    {
                        Debug.Assert(mask.Key.Count < 2, "we don't support multi-tags yet");
                        masks.Add(mask.Key.ToString(), mask.Value);
                    }
                }

                //
                // Outgoing Attachments:
                {
                    var legacy = context.DeserializeTagLookup(() => new OutgoingAttachment(context));
                    outgoingAttachments = new OrderedDictionary<string, OutgoingAttachment>();
                    foreach (var outgoingAttachment in legacy)
                    {
                        Debug.Assert(outgoingAttachment.Key.Count < 2, "we don't support multi-tags yet");
                        outgoingAttachments.Add(outgoingAttachment.Key.ToString(),
                            outgoingAttachment.Value);
                    }
                }
            }

            incomingAttachments = context.DeserializeTagLookup(() => context.br.ReadPosition());

            int triggerCount = context.br.ReadInt32();
            if (triggerCount > 0)
            {
                triggers = new List<string>(triggerCount);
                for (var i = 0; i < triggerCount; i++)
                    triggers.Add(context.br.ReadString());
            }

            attachAtLayer = context.br.ReadInt32();
            canDrawLayersAboveSortedAttachees = context.br.ReadBoolean();

            cue = context.br.ReadNullableString();
        }

        #endregion
    }
}
