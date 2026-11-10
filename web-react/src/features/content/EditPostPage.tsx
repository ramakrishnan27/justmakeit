import PostEditorPage from './PostEditorPage';

/** /content-studio/:id: the same page for an existing post. Needs contentstudio.view; without contentstudio.create it is read only. */
export default function EditPostPage() {
  return <PostEditorPage mode="edit" />;
}
