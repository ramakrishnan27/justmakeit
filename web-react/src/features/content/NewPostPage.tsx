import PostEditorPage from './PostEditorPage';

/** /content-studio/new: the Create page. Needs contentstudio.create (the route checks it, and so does the server). */
export default function NewPostPage() {
  return <PostEditorPage mode="new" />;
}
