import { useEffect } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { getToken } from '../api/client';

export function AuthGuard({ children }: { children: React.ReactNode }) {
  const navigate = useNavigate();
  const location = useLocation();

  useEffect(() => {
    if (!getToken()) {
      navigate('/login', { replace: true, state: { from: location.pathname } });
    }
  }, [location.pathname, navigate]);

  return <>{children}</>;
}
