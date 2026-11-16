import { Link } from 'react-router';

export function NotFoundPage({ message = 'There is no page here.' }: { message?: string }) {
  return (
    <div className="page-message">
      <p>{message}</p>
      <Link to="/rota">Go to this week's rota</Link>
    </div>
  );
}
