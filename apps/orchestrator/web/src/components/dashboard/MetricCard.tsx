import { motion, type Variants } from 'framer-motion'
import { Card, CardContent } from '@/components/ui/card'

interface MetricCardProps {
  label: string
  value: string
  index?: number
}

const cardVariants: Variants = {
  hidden: { opacity: 0, y: 20 },
  visible: (i: number) => ({
    opacity: 1,
    y: 0,
    transition: {
      delay: i * 0.1,
      duration: 0.4,
      ease: [0.25, 0.1, 0.25, 1],
    },
  }),
}

export function MetricCard({ label, value, index = 0 }: MetricCardProps) {
  return (
    <motion.div
      custom={index}
      initial="hidden"
      animate="visible"
      variants={cardVariants}
    >
      <Card className="rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-5 shadow-[var(--surface-shadow)]">
        <CardContent className="p-0">
          <p className="text-xs uppercase tracking-wide text-[var(--text-soft)]">{label}</p>
          <p className="mt-2 text-2xl font-semibold text-[var(--text-strong)]">{value}</p>
        </CardContent>
      </Card>
    </motion.div>
  )
}