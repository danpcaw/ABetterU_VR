-- Built during the hackathon (Oct 29): lets the VR app's assistant attach an image to a feed post.
-- Public bucket (so the feed can show the image), 2 MB limit, JPEG/PNG only, and a user may upload
-- only into a folder named after their own user id.
insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values ('atlas-post-images', 'atlas-post-images', true, 2097152, array['image/jpeg', 'image/png'])
on conflict (id) do nothing;

create policy "Atlas post images owner insert"
on storage.objects for insert to authenticated
with check (bucket_id = 'atlas-post-images' and (storage.foldername(name))[1] = (auth.uid())::text);
