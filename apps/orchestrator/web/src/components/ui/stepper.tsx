import { cn } from '@/lib/utils'

interface Step {
  id: string
  label: string
}

interface StepperProps {
  steps: Step[]
  activeStep: number
  className?: string
}

export function Stepper({ steps, activeStep, className }: StepperProps) {
  return (
    <div className={cn('flex items-center w-full', className)}>
      {steps.map((step, index) => {
        const isCompleted = index < activeStep
        const isActive = index === activeStep
        const isPending = index > activeStep

        return (
          <div key={step.id} className="flex items-center flex-1 last:flex-none">
            <div className="flex flex-col items-center">
              <div
                className={cn(
                  'flex items-center justify-center w-8 h-8 rounded-full border-2 text-sm font-medium transition-colors',
                  isCompleted && 'bg-green-600 border-green-600 text-white',
                  isActive && 'bg-blue-600 border-blue-600 text-white animate-pulse',
                  isPending && 'bg-gray-100 border-gray-300 text-gray-500'
                )}
              >
                {isCompleted ? (
                  <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={3} d="M5 13l4 4L19 7" />
                  </svg>
                ) : (
                  <span>{index + 1}</span>
                )}
              </div>
              <span
                className={cn(
                  'mt-1.5 text-xs font-medium text-center max-w-[80px]',
                  isCompleted && 'text-green-700',
                  isActive && 'text-blue-700',
                  isPending && 'text-gray-400'
                )}
              >
                {step.label}
              </span>
            </div>
            {index < steps.length - 1 && (
              <div
                className={cn(
                  'flex-1 h-0.5 mx-2 transition-colors',
                  isCompleted ? 'bg-green-600' : 'bg-gray-200'
                )}
              />
            )}
          </div>
        )
      })}
    </div>
  )
}