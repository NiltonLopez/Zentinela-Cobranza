import { render, screen, fireEvent } from '@testing-library/react';
import { ZButton } from '../../components/ZButton';

describe('ZButton', () => {
  it('renderiza el texto del botón', () => {
    render(<ZButton>Descargar Excel</ZButton>);
    expect(screen.getByText('Descargar Excel')).toBeInTheDocument();
  });

  it('variante primary tiene fondo naranja de la marca', () => {
    render(<ZButton variant="primary">Acción</ZButton>);
    const btn = screen.getByRole('button');
    expect(btn).toHaveStyle({ background: 'var(--z-brand)' });
  });

  it('variante ghost tiene fondo transparente', () => {
    render(<ZButton variant="ghost">Cancelar</ZButton>);
    const btn = screen.getByRole('button');
    expect(btn).toHaveStyle({ background: 'transparent' });
  });

  it('llama al onClick cuando se hace clic', () => {
    const handleClick = vi.fn();
    render(<ZButton onClick={handleClick}>Clic</ZButton>);
    fireEvent.click(screen.getByRole('button'));
    expect(handleClick).toHaveBeenCalledTimes(1);
  });

  it('botón deshabilitado no responde al clic', () => {
    const handleClick = vi.fn();
    render(<ZButton onClick={handleClick} disabled>Inactivo</ZButton>);
    fireEvent.click(screen.getByRole('button'));
    expect(handleClick).not.toHaveBeenCalled();
  });
});
