﻿// Copyright (c) 2007-2017 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-framework/master/LICENCE

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Caching;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Textures;
using OpenTK;
using OpenTK.Graphics;
using osu.Framework.Allocation;
using osu.Framework.IO.Stores;
using osu.Framework.Graphics.Transforms;

namespace osu.Framework.Graphics.Sprites
{
    public class SpriteText : FlowContainer
    {
        private static readonly char[] default_fixed_width_exceptions = { '.', ':' };

        /// <summary>
        /// An array of characters which should not get a fixed width in a <see cref="FixedWidth"/> instance.
        /// </summary>
        protected virtual char[] FixedWidthExceptionCharacters => default_fixed_width_exceptions;

        /// <summary>
        /// Decide whether we want to make our SpriteText's vertical size to be <see cref="TextHeight"/> (the full height) or precisely the size of used characters.
        /// Set to false to allow better centering of individual characters/numerals/etc.
        /// </summary>
        public bool UseFullGlyphHeight = true;

        public override bool IsPresent => base.IsPresent && !string.IsNullOrEmpty(text);

        protected sealed override bool CanChangeFlowStrategy => false;

        public bool AllowMultiline
        {
            get { return ((FillFlowStrategy)FlowStrategy).VerticalFlow != VerticalDirection.None; }
            set
            {
                if (value)
                    ((FillFlowStrategy)FlowStrategy).VerticalFlow = VerticalDirection.TopToBottom;
                else
                    ((FillFlowStrategy)FlowStrategy).VerticalFlow = VerticalDirection.None;
            }
        }

        public Vector2 Spacing
        {
            get { return ((FillFlowStrategy)FlowStrategy).Spacing; }
            set
            {
                if (Spacing == value)
                    return;

                ((FillFlowStrategy)FlowStrategy).Spacing = value;
            }
        }

        private string font;

        public string Font
        {
            get { return font; }
            set
            {
                font = value;
                internalSize.Invalidate();
            }
        }

        private bool shadow;
        public bool Shadow
        {
            get { return shadow; }
            set
            {
                if (shadow == value) return;

                shadow = value;
                internalSize.Invalidate(); // Trigger a layout refresh
            }
        }


        private Color4 shadowColour = new Color4(0f, 0f, 0f, 0.2f);
        public Color4 ShadowColour
        {
            get { return shadowColour; }
            set
            {
                shadowColour = value;
                if (shadow)
                    internalSize.Invalidate();
            }
        }

        private Cached<Vector2> internalSize = new Cached<Vector2>();

        private float spaceWidth;

        private TextureStore store;

        public override bool HandleInput => false;

        public SpriteText()
        {
            AutoSizeAxes = Axes.Both;
        }

        const float default_text_size = 20;

        private float textSize = default_text_size;

        public float TextSize
        {
            get
            {
                return textSize;
            }
            set
            {
                if (textSize == value) return;

                textSize = value;

                foreach (Drawable d in Children)
                    d.Scale = new Vector2(textSize);
            }
        }

        [BackgroundDependencyLoader]
        private void load(FontStore store)
        {
            this.store = store;

            spaceWidth = CreateCharacterDrawable('.')?.DrawWidth * 2 ?? default_text_size;

            if (!string.IsNullOrEmpty(text))
            {
                //this is used to prepare the initial string (useful for intial preloading).
                foreach (char c in text)
                    if (!char.IsWhiteSpace(c)) CreateCharacterDrawable(c);
            }
        }

        private string text;

        public string Text
        {
            get { return text; }
            set
            {
                if (text == value)
                    return;

                text = value;
                internalSize.Invalidate();
            }
        }

        private float? constantWidth;
        public bool FixedWidth;

        public void TransformSpacingTo(Vector2 newSpacing, double duration = 0, EasingTypes easing = EasingTypes.None)
        {
            UpdateTransformsOfType(typeof(TransformSpacing));
            TransformVectorTo(Spacing, newSpacing, duration, easing, new TransformSpacing());
        }

        public class TransformSpacing : TransformVector
        {
            public override void Apply(Drawable d)
            {
                base.Apply(d);
                SpriteText st = (SpriteText)d;
                st.Spacing = CurrentValue;
            }
        }

        protected override void Update()
        {
            base.Update();
            refreshLayout();
        }

        string lastText;

        private void refreshLayout()
        {
            if (internalSize.EnsureValid()) return;

            internalSize.Refresh(delegate
            {
                if (FixedWidth && !constantWidth.HasValue)
                    constantWidth = CreateCharacterDrawable('D').DrawWidth;

                //keep sprites which haven't changed since last layout.
                List<Drawable> keepDrawables = new List<Drawable>();
                int length = Math.Min(lastText?.Length ?? 0, text?.Length ?? 0);

                keepDrawables.AddRange(Children.TakeWhile((n, i) => i < length && lastText[i] == text[i]));
                Remove(keepDrawables);
                Clear();

                foreach (var k in keepDrawables)
                    Add(k);

                //adjust shadow alpha based on highest component intensity to avoid muddy display of darker text.
                //squared result for quadratic fall-off seems to give the best result.
                var avgColour = (Color4)ColourInfo.AverageColour;
                float shadowAlpha = (float)Math.Pow(Math.Max(Math.Max(avgColour.R, avgColour.G), avgColour.B), 2);

                for (int index = keepDrawables.Count; index < text.Length; index++)
                {
                    char c = text[index];

                    bool fixedWidth = FixedWidth && !FixedWidthExceptionCharacters.Contains(c);

                    Drawable d;

                    if (char.IsWhiteSpace(c))
                    {
                        float width = fixedWidth ? constantWidth.GetValueOrDefault() : spaceWidth;

                        switch ((int)c)
                        {
                            case 0x3000: //double-width space
                                width *= 2;
                                break;
                        }

                        d = new Container
                        {
                            Size = new Vector2(width),
                            Scale = new Vector2(TextSize),
                            Colour = Color4.Transparent,
                        };
                    }
                    else
                    {
                        d = CreateCharacterDrawable(c);

                        if (fixedWidth)
                        {
                            d.Anchor = Anchor.TopCentre;
                            d.Origin = Anchor.TopCentre;
                        }

                        var ctn = new Container
                        {
                            Size = new Vector2(fixedWidth ? constantWidth.GetValueOrDefault() : d.DrawSize.X, UseFullGlyphHeight ? 1 : d.DrawSize.Y),
                            Scale = new Vector2(TextSize),
                            Children = new[] { d }
                        };

                        if (shadow)
                        {
                            Drawable shadowDrawable = CreateCharacterDrawable(c);
                            shadowDrawable.Position = new Vector2(0, 0.06f);
                            shadowDrawable.Anchor = d.Anchor;
                            shadowDrawable.Origin = d.Origin;
                            shadowDrawable.Alpha = shadowAlpha;
                            shadowDrawable.Colour = shadowColour;
                            shadowDrawable.Depth = float.MaxValue;
                            ctn.Add(shadowDrawable);
                        }

                        d = ctn;
                    }

                    Add(d);
                }

                lastText = text;
                return Vector2.Zero;
            });
        }

        protected virtual Drawable CreateFallbackCharacterDrawable() => new Box
        {
            Origin = Anchor.Centre,
            Anchor = Anchor.Centre,
            Scale = new Vector2(0.7f)
        };

        protected virtual Drawable CreateCharacterDrawable(char c)
        {
            var tex = GetTextureForCharacter(c);
            if (tex != null)
                return new Sprite { Texture = tex };

            return CreateFallbackCharacterDrawable();
        }

        protected Texture GetTextureForCharacter(char c)
        {
            return store?.Get(getTextureName(c)) ?? store?.Get(getTextureName(c, false));
        }

        private string getTextureName(char c, bool useFont = true) => !useFont || string.IsNullOrEmpty(Font) ? c.ToString() : $@"{Font}/{c}";

        public override string ToString()
        {
            return $@"""{Text}"" " + base.ToString();
        }
    }
}
