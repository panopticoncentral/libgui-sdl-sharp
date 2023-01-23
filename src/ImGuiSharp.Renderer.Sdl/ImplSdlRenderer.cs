﻿using System;

using ImguiSharp;

using SdlSharp;
using SdlSharp.Graphics;

using static ImguiSharp.Native;

namespace ImGuiSharp.Renderer.Sdl
{
    public static unsafe class ImplSdlRenderer
    {
        private static Dictionary<nuint, Data>? s_dataDictionary;
        private static Dictionary<nuint, Data> DataDictionary => s_dataDictionary ??= new Dictionary<nuint, Data>();

        private struct Data
        {
            public SdlSharp.Graphics.Renderer _sdlRenderer;
            public Texture? _fontTexture;

            public Data(SdlSharp.Graphics.Renderer sdlRenderer)
            {
                _sdlRenderer = sdlRenderer;
            }
        };

        private static Data GetBackendData() =>
            Imgui.GetCurrentContext() != null
                ? DataDictionary[Imgui.GetIo().BackendRendererUserData]
                : throw new InvalidOperationException();

        public static bool Init(SdlSharp.Graphics.Renderer renderer)
        {
            var io = Imgui.GetIo();

            if (io.BackendRendererUserData != 0)
            {
                throw new InvalidOperationException();
            }

            var bd = new Data(renderer);
            DataDictionary[(nuint)bd.GetHashCode()] = bd;

            io.BackendRendererUserData = (nuint)bd.GetHashCode();
            io.BackendRendererName = "imgui_impl_sdlrenderer";
            io.BackendOptions |= BackendOptions.RendererHasVtxOffset;

            return true;
        }

        private static void Shutdown()
        {
            var bd = GetBackendData();
            var io = Imgui.GetIo();

            DestroyDeviceObjects();

            io.BackendRendererName = null;
            io.BackendRendererUserData = 0;
            _ = DataDictionary.Remove((nuint)bd.GetHashCode());
        }

        private static void SetupRenderState()
        {
            var bd = GetBackendData();

            bd._sdlRenderer.Viewport = null;
            bd._sdlRenderer.ClippingRectangle = null;
        }

        public static void NewFrame()
        {
            var bd = GetBackendData();

            if (bd._fontTexture == null)
            {
                _ = CreateDeviceObjects();
            }
        }

#if false
void ImGui_ImplSDLRenderer_RenderDrawData(ImDrawData* draw_data)
{
	ImGui_ImplSDLRenderer_Data* bd = ImGui_ImplSDLRenderer_GetBackendData();

	// If there's a scale factor set by the user, use that instead
    // If the user has specified a scale factor to SDL_Renderer already via SDL_RenderSetScale(), SDL will scale whatever we pass
    // to SDL_RenderGeometryRaw() by that scale factor. In that case we don't want to be also scaling it ourselves here.
    float rsx = 1.0f;
	float rsy = 1.0f;
	SDL_RenderGetScale(bd->SDLRenderer, &rsx, &rsy);
    ImVec2 render_scale;
	render_scale.x = (rsx == 1.0f) ? draw_data->FramebufferScale.x : 1.0f;
	render_scale.y = (rsy == 1.0f) ? draw_data->FramebufferScale.y : 1.0f;

	// Avoid rendering when minimized, scale coordinates for retina displays (screen coordinates != framebuffer coordinates)
	int fb_width = (int)(draw_data->DisplaySize.x * render_scale.x);
	int fb_height = (int)(draw_data->DisplaySize.y * render_scale.y);
	if (fb_width == 0 || fb_height == 0)
		return;

    // Backup SDL_Renderer state that will be modified to restore it afterwards
    struct BackupSDLRendererState
    {
        SDL_Rect    Viewport;
        bool        ClipEnabled;
        SDL_Rect    ClipRect;
    };
    BackupSDLRendererState old = {};
    old.ClipEnabled = SDL_RenderIsClipEnabled(bd->SDLRenderer) == SDL_TRUE;
    SDL_RenderGetViewport(bd->SDLRenderer, &old.Viewport);
    SDL_RenderGetClipRect(bd->SDLRenderer, &old.ClipRect);

	// Will project scissor/clipping rectangles into framebuffer space
	ImVec2 clip_off = draw_data->DisplayPos;         // (0,0) unless using multi-viewports
	ImVec2 clip_scale = render_scale;

    // Render command lists
    ImGui_ImplSDLRenderer_SetupRenderState();
    for (int n = 0; n < draw_data->CmdListsCount; n++)
    {
        const ImDrawList* cmd_list = draw_data->CmdLists[n];
        const ImDrawVert* vtx_buffer = cmd_list->VtxBuffer.Data;
        const ImDrawIdx* idx_buffer = cmd_list->IdxBuffer.Data;

        for (int cmd_i = 0; cmd_i < cmd_list->CmdBuffer.Size; cmd_i++)
        {
            const ImDrawCmd* pcmd = &cmd_list->CmdBuffer[cmd_i];
            if (pcmd->UserCallback)
            {
                // User callback, registered via ImDrawList::AddCallback()
                // (ImDrawCallback_ResetRenderState is a special callback value used by the user to request the renderer to reset render state.)
                if (pcmd->UserCallback == ImDrawCallback_ResetRenderState)
                    ImGui_ImplSDLRenderer_SetupRenderState();
                else
                    pcmd->UserCallback(cmd_list, pcmd);
            }
            else
            {
                // Project scissor/clipping rectangles into framebuffer space
                ImVec2 clip_min((pcmd->ClipRect.x - clip_off.x) * clip_scale.x, (pcmd->ClipRect.y - clip_off.y) * clip_scale.y);
                ImVec2 clip_max((pcmd->ClipRect.z - clip_off.x) * clip_scale.x, (pcmd->ClipRect.w - clip_off.y) * clip_scale.y);
                if (clip_min.x < 0.0f) { clip_min.x = 0.0f; }
                if (clip_min.y < 0.0f) { clip_min.y = 0.0f; }
                if (clip_max.x > (float)fb_width) { clip_max.x = (float)fb_width; }
                if (clip_max.y > (float)fb_height) { clip_max.y = (float)fb_height; }
                if (clip_max.x <= clip_min.x || clip_max.y <= clip_min.y)
                    continue;

                SDL_Rect r = { (int)(clip_min.x), (int)(clip_min.y), (int)(clip_max.x - clip_min.x), (int)(clip_max.y - clip_min.y) };
                SDL_RenderSetClipRect(bd->SDLRenderer, &r);

                const float* xy = (const float*)(const void*)((const char*)(vtx_buffer + pcmd->VtxOffset) + IM_OFFSETOF(ImDrawVert, pos));
                const float* uv = (const float*)(const void*)((const char*)(vtx_buffer + pcmd->VtxOffset) + IM_OFFSETOF(ImDrawVert, uv));
                const SDL_Color* color = (const SDL_Color*)(const void*)((const char*)(vtx_buffer + pcmd->VtxOffset) + IM_OFFSETOF(ImDrawVert, col)); // SDL 2.0.19+

                // Bind texture, Draw
				SDL_Texture* tex = (SDL_Texture*)pcmd->GetTexID();
                SDL_RenderGeometryRaw(bd->SDLRenderer, tex,
                    xy, (int)sizeof(ImDrawVert),
                    color, (int)sizeof(ImDrawVert),
                    uv, (int)sizeof(ImDrawVert),
                    cmd_list->VtxBuffer.Size - pcmd->VtxOffset,
                    idx_buffer + pcmd->IdxOffset, pcmd->ElemCount, sizeof(ImDrawIdx));
            }
        }
    }

    // Restore modified SDL_Renderer state
    SDL_RenderSetViewport(bd->SDLRenderer, &old.Viewport);
    SDL_RenderSetClipRect(bd->SDLRenderer, old.ClipEnabled ? &old.ClipRect : nullptr);
}
#endif

        private static bool CreateFontsTexture()
        {
            var io = Imgui.GetIo();
            var bd = GetBackendData();

            io.Fonts.GetTextureDataAsRgba32(out var pixels, out var width, out var height, out var _);
            SdlSharp.Graphics.Size size = new(width, height);

            bd._fontTexture = bd._sdlRenderer.CreateTexture(EnumeratedPixelFormat.Abgr8888, TextureAccess.Static, size);
            if (bd._fontTexture == null)
            {
                Log.Error("error creating texture");
                return false;
            }
            bd._fontTexture.Update(null, pixels, 4 * size.Width);
            bd._fontTexture.BlendMode = BlendMode.Blend;
            bd._fontTexture.ScaleMode = ScaleMode.Linear;

            io.Fonts.SetTextureId(new(bd._fontTexture.Id));

            return true;
        }

        private static void DestroyFontsTexture()
        {
            var io = Imgui.GetIo();
            var bd = GetBackendData();
            if (bd._fontTexture != null)
            {
                io.Fonts.SetTextureId(new(0));
                bd._fontTexture.Dispose();
                bd._fontTexture = null;
            }
        }

        private static bool CreateDeviceObjects() => CreateFontsTexture();

        private static void DestroyDeviceObjects() => DestroyFontsTexture();
    }
}
